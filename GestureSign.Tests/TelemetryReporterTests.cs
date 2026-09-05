using GestureSign.Common.Telemetry;
using GestureSign.Common.Updates;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace GestureSign.Tests
{
    public class TelemetryReporterTests
    {
        [Fact]
        public async Task OfflineStartupRecoversAndDrainsQueueWithoutPerEventRepositoryRequests()
        {
            using var directory = new TemporaryDirectory();
            var delays = new ControlledDelay();
            var handler = new RecordingHandler();
            int loads = 0;
            using var reporter = CreateReporter(directory.Path, handler, (_, token) =>
            {
                if (Interlocked.Increment(ref loads) == 1)
                    throw new HttpRequestException("offline");
                return Task.FromResult(Configuration("http://healthy.invalid", 1));
            }, delays.WaitAsync);

            reporter.Track("application_started");
            reporter.Start();
            reporter.Start();
            (TimeSpan duration, TaskCompletionSource gate) = await delays.NextAsync();
            Assert.Equal(TimeSpan.FromMinutes(10), duration);
            Assert.Equal(1, Volatile.Read(ref loads));
            Assert.Empty(handler.Requests);

            gate.SetResult();
            await handler.NextAsync();
            reporter.Track("manual_update_check_requested");
            await handler.NextAsync();

            Assert.Equal(2, loads);
            Assert.Equal(new[] { "application_started", "manual_update_check_requested" },
                handler.Requests.Select(request => request.Name));
        }

        [Fact]
        public async Task FailedServerReloadsConfigurationAndRetriesSameEventOnNewServer()
        {
            using var directory = new TemporaryDirectory();
            var delays = new ControlledDelay();
            var handler = new RecordingHandler(request => request.RequestUri.Host == "old.invalid"
                ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted);
            int loads = 0;
            using var reporter = CreateReporter(directory.Path, handler, (_, token) =>
                Task.FromResult(Interlocked.Increment(ref loads) == 1
                    ? Configuration("http://old.invalid", 1)
                    : Configuration("http://new.invalid", 2)), delays.WaitAsync);

            reporter.Track("application_started");
            reporter.Start();
            await handler.NextAsync();
            (TimeSpan duration, TaskCompletionSource gate) = await delays.NextAsync();
            Assert.Equal(TimeSpan.FromMinutes(10), duration);
            Assert.Equal(1, loads);
            gate.SetResult();
            await handler.NextAsync();

            Request[] requests = handler.Requests.ToArray();
            Assert.Equal(new[] { "old.invalid", "new.invalid" }, requests.Select(request => request.Host));
            Assert.Equal(requests[0].EventId, requests[1].EventId);
            Assert.Equal(requests[0].ClientId, requests[1].ClientId);
            Assert.Equal(2, loads);
        }

        [Fact]
        public async Task DisposingDuringRecoveryCancelsWaitAndPreventsRestart()
        {
            using var directory = new TemporaryDirectory();
            var delays = new ControlledDelay();
            int loads = 0;
            using var reporter = CreateReporter(directory.Path, new RecordingHandler(), (_, token) =>
            {
                Interlocked.Increment(ref loads);
                throw new HttpRequestException("offline");
            }, delays.WaitAsync);
            reporter.Start();
            await delays.NextAsync();

            reporter.Dispose();
            await reporter.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            reporter.Start();

            Assert.Equal(1, loads);
        }

        [LiveTelemetryFact]
        public async Task LiveRepositoryConfigurationDeliversDeploymentCheckToServer()
        {
            using var directory = new TemporaryDirectory();
            using var forwardingClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            var accepted = new TaskCompletionSource<HttpStatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errors = new ConcurrentQueue<Exception>();
            using var handler = new ForwardingHandler(forwardingClient, accepted);
            using var reporter = new TelemetryReporter(GitHubRepository.Parse("Autumn-one/TouchPilot"),
                directory.Path, "8.2.0", "installer", "win-x64", errors.Enqueue, new HttpClient(handler));

            reporter.Track("deployment_check", new Dictionary<string, string> { ["source"] = "integration_test" });
            reporter.Start();
            Assert.Equal(HttpStatusCode.Accepted,
                await accepted.Task.WaitAsync(TimeSpan.FromSeconds(60)));
            Assert.Empty(errors);
            Assert.True(File.Exists(Path.Combine(directory.Path, "endpoint-config.json")));
        }

        private static TelemetryReporter CreateReporter(string directory, HttpMessageHandler handler,
            Func<string, CancellationToken, Task<TelemetryConfigurationResult>> loader,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            return new TelemetryReporter(GitHubRepository.Parse("Autumn-one/TouchPilot"), directory,
                "8.2.0", "installer", "win-x64", _ => { }, new HttpClient(handler), loader, delay);
        }

        private static TelemetryConfigurationResult Configuration(string address, long revision)
        {
            return new TelemetryConfigurationResult(new TelemetryConfiguration
            {
                Repository = "Autumn-one/TouchPilot",
                Revision = revision,
                PublishedAtUtc = DateTimeOffset.UtcNow,
                Endpoints = new List<TelemetryEndpoint> { new TelemetryEndpoint { BaseAddress = address, Port = 4318 } }
            }, "test", false, 0);
        }

        private sealed class ControlledDelay
        {
            private readonly Channel<(TimeSpan, TaskCompletionSource)> _waits =
                Channel.CreateUnbounded<(TimeSpan, TaskCompletionSource)>();

            public async Task WaitAsync(TimeSpan duration, CancellationToken token)
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await _waits.Writer.WriteAsync((duration, gate), token);
                await gate.Task.WaitAsync(token);
            }

            public Task<(TimeSpan, TaskCompletionSource)> NextAsync()
            {
                return _waits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        private sealed record Request(string Host, string EventId, string ClientId, string Name);

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpStatusCode> _status;
            private readonly Channel<bool> _completed = Channel.CreateUnbounded<bool>();
            public ConcurrentQueue<Request> Requests { get; } = new ConcurrentQueue<Request>();

            public RecordingHandler(Func<HttpRequestMessage, HttpStatusCode> status = null)
            {
                _status = status ?? (_ => HttpStatusCode.Accepted);
            }

            public Task NextAsync() => _completed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                using JsonDocument json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
                JsonElement root = json.RootElement;
                Requests.Enqueue(new Request(request.RequestUri.Host, root.GetProperty("eventId").GetString(),
                    root.GetProperty("clientId").GetString(), root.GetProperty("name").GetString()));
                var response = new HttpResponseMessage(_status(request));
                _completed.Writer.TryWrite(true);
                return response;
            }
        }

        private sealed class ForwardingHandler : HttpMessageHandler
        {
            private readonly HttpClient _client;
            private readonly TaskCompletionSource<HttpStatusCode> _accepted;

            public ForwardingHandler(HttpClient client, TaskCompletionSource<HttpStatusCode> accepted)
            {
                _client = client;
                _accepted = accepted;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                using var forward = new HttpRequestMessage(request.Method, request.RequestUri)
                {
                    Content = new StringContent(await request.Content.ReadAsStringAsync(token),
                        System.Text.Encoding.UTF8, "application/json")
                };
                HttpResponseMessage response = await _client.SendAsync(forward, token);
                _accepted.TrySetResult(response.StatusCode);
                return response;
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "TouchPilot.ReporterTests." + Guid.NewGuid().ToString("N"));

            public void Dispose()
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, true);
            }
        }
    }

    public sealed class LiveTelemetryFactAttribute : FactAttribute
    {
        public LiveTelemetryFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("TOUCHPILOT_LIVE_TELEMETRY_TEST") != "1")
                Skip = "Set TOUCHPILOT_LIVE_TELEMETRY_TEST=1 to send one deployment_check to the configured server.";
        }
    }
}
