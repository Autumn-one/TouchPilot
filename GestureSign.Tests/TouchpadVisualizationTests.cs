using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.ControlPanel.UserControls;
using GestureSign.ControlPanel.Visualization;
using GestureSign.Daemon.Visualization;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace GestureSign.Tests
{
    public class TouchpadVisualizationTests
    {
        [Fact]
        public async Task ProtocolRoundTripsTouchpadFrame()
        {
            var expected = new TouchpadVisualizationFrame(12345, new[]
            {
                new TouchpadContact(7, DeviceStates.Tip, 0.25, 0.75),
                new TouchpadContact(9, DeviceStates.None, 0.8, 0.1)
            });
            using var stream = new MemoryStream();

            await TouchpadVisualizationProtocol.WriteHandshakeAsync(stream, CancellationToken.None);
            await TouchpadVisualizationProtocol.WriteFrameAsync(stream, expected, CancellationToken.None);
            stream.Position = 0;
            await TouchpadVisualizationProtocol.ReadHandshakeAsync(stream, CancellationToken.None);
            TouchpadVisualizationFrame actual = await TouchpadVisualizationProtocol.ReadFrameAsync(
                stream, CancellationToken.None);

            Assert.Equal(expected.TimestampMilliseconds, actual.TimestampMilliseconds);
            Assert.Equal(2, actual.Contacts.Count);
            Assert.Equal(7, actual.Contacts[0].ContactIdentifier);
            Assert.Equal(DeviceStates.Tip, actual.Contacts[0].State);
            Assert.Equal(0.25, actual.Contacts[0].NormalizedX, 5);
            Assert.Equal(0.75, actual.Contacts[0].NormalizedY, 5);
            Assert.Equal(DeviceStates.None, actual.Contacts[1].State);
        }

        [Fact]
        public async Task ProtocolRejectsUnsupportedHandshake()
        {
            using var stream = new MemoryStream(new byte[] { (byte)'G', (byte)'S', (byte)'T', (byte)'V', 2 });

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await TouchpadVisualizationProtocol.ReadHandshakeAsync(stream, CancellationToken.None));
        }

        [Fact]
        public async Task LatestFrameBufferDropsSupersededFrame()
        {
            var buffer = new LatestTouchpadFrameBuffer();
            buffer.Publish(Frame(1, 0.1));
            buffer.Publish(Frame(2, 0.2));

            TouchpadVisualizationFrame actual = await buffer.ReadAsync(CancellationToken.None);

            Assert.Equal(2, actual.TimestampMilliseconds);
            Assert.Equal(0.2, actual.Contacts[0].NormalizedX, 5);
        }

        [Fact]
        public async Task ServerSubscribesOnlyWhileClientIsConnected()
        {
            string pipeName = "GestureSignTouchpadVisualizationTest-" + Guid.NewGuid().ToString("N");
            var source = new FakeFrameSource();
            using var server = new TouchpadVisualizationServer(source, pipeName);
            server.Start();
            Assert.Equal(0, source.SubscriberCount);

            var client = new NamedPipeClientStream(".", NamedPipe.GetUserPipeName(pipeName),
                PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(3000);
            await TouchpadVisualizationProtocol.ReadHandshakeAsync(client, CancellationToken.None);
            await TouchpadVisualizationProtocol.ReadFrameAsync(client, CancellationToken.None);
            Assert.True(SpinWait.SpinUntil(() => source.SubscriberCount == 1, 3000));

            source.Publish(Frame(10, 0.4));
            TouchpadVisualizationFrame received = await TouchpadVisualizationProtocol.ReadFrameAsync(
                client, CancellationToken.None);
            Assert.Equal(10, received.TimestampMilliseconds);
            Assert.Equal(0.4, received.Contacts[0].NormalizedX, 5);

            client.Dispose();
            Assert.True(SpinWait.SpinUntil(() => source.SubscriberCount == 0, 3000));
        }

        [Fact]
        public async Task ClientReceivesFramesAndDisconnectsWithoutBlocking()
        {
            string pipeName = "GestureSignTouchpadVisualizationClientTest-" + Guid.NewGuid().ToString("N");
            var source = new FakeFrameSource();
            using var server = new TouchpadVisualizationServer(source, pipeName);
            using var client = new TouchpadVisualizationClient(pipeName, TimeSpan.FromMilliseconds(20));
            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var received = new TaskCompletionSource<TouchpadVisualizationFrame>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            client.ConnectionChanged += (sender, value) =>
            {
                if (value)
                    connected.TrySetResult(true);
            };
            client.FrameReceived += (sender, frame) =>
            {
                if (frame.TimestampMilliseconds == 42)
                    received.TrySetResult(frame);
            };

            server.Start();
            client.Start();
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(SpinWait.SpinUntil(() => source.SubscriberCount == 1, 3000));
            source.Publish(Frame(42, 0.6));

            TouchpadVisualizationFrame actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0.6, actual.Contacts[0].NormalizedX, 5);
            client.Dispose();
            Assert.True(SpinWait.SpinUntil(() => source.SubscriberCount == 0, 3000));
        }

        [Fact]
        public void VisualizationStateReleasesContactsMissingFromNextFrame()
        {
            var state = new TouchpadVisualizationState();
            state.ApplyFrame(new TouchpadVisualizationFrame(10, new[]
            {
                new TouchpadContact(1, DeviceStates.Tip, 0.2, 0.3),
                new TouchpadContact(2, DeviceStates.Tip, 0.7, 0.8)
            }));

            state.ApplyFrame(new TouchpadVisualizationFrame(20, new[]
            {
                new TouchpadContact(1, DeviceStates.Tip, 0.25, 0.35)
            }));

            Assert.Equal(1, state.ActiveContactCount);
            Assert.True(Assert.Single(state.Traces, trace => trace.ContactIdentifier == 1).IsActive);
            Assert.False(Assert.Single(state.Traces, trace => trace.ContactIdentifier == 2).IsActive);
        }

        [Fact]
        public void VisualizationStateStartsFreshTrailWhenIdentifierIsReused()
        {
            var state = new TouchpadVisualizationState();
            state.ApplyFrame(Frame(10, 0.1));
            state.ApplyFrame(new TouchpadVisualizationFrame(20, Array.Empty<TouchpadContact>()));
            state.ApplyFrame(Frame(30, 0.9));

            TouchpadContactTrace trace = Assert.Single(state.Traces);
            TouchpadTracePoint point = Assert.Single(trace.Points);
            Assert.True(trace.IsActive);
            Assert.Equal(0.9, point.NormalizedX, 5);
        }

        [Fact]
        public void VisualizationStateBoundsAndExpiresTrails()
        {
            var state = new TouchpadVisualizationState();
            for (int i = 0; i < TouchpadVisualizationState.MaximumTrailPointCount + 10; i++)
                state.ApplyFrame(Frame(i * 5, i / 100d));

            TouchpadContactTrace trace = Assert.Single(state.Traces);
            Assert.Equal(TouchpadVisualizationState.MaximumTrailPointCount, trace.Points.Count);

            long releasedAt = 500;
            state.ApplyFrame(new TouchpadVisualizationFrame(releasedAt, Array.Empty<TouchpadContact>()));
            state.Advance(releasedAt + TouchpadVisualizationState.TrailLifetimeMilliseconds);
            Assert.Empty(state.Traces);
        }

        [Fact]
        public void VisualizerRendersContactAtNormalizedPosition()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    const int width = 620;
                    const int height = 360;
                    var visualizer = new TouchpadVisualizer
                    {
                        Width = width,
                        Height = height,
                        SurfaceBrush = Brushes.Black,
                        OutlineBrush = Brushes.Gray,
                        EdgeAreaBrush = new SolidColorBrush(Color.FromArgb(80, 255, 80, 40)),
                        EdgeBoundaryBrush = Brushes.Orange,
                        EmptyTextBrush = Brushes.White,
                        IsConnected = true
                    };
                    visualizer.Measure(new Size(width, height));
                    visualizer.Arrange(new Rect(0, 0, width, height));
                    visualizer.UpdateFrame(new TouchpadVisualizationFrame(100, new[]
                    {
                        new TouchpadContact(0, DeviceStates.Tip, 0.25, 0.75)
                    }));
                    visualizer.UpdateLayout();

                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visualizer);
                    var pixels = new byte[width * height * 4];
                    bitmap.CopyPixels(pixels, width * 4, 0);

                    int centerX = (int)Math.Round(1 + (width - 2) * 0.25);
                    int centerY = (int)Math.Round(1 + (height - 2) * 0.75);
                    bool foundContactColor = false;
                    int maximumBlue = 0;
                    int maximumGreen = 0;
                    int maximumRed = 0;
                    int nonBlackPixelCount = 0;
                    int contactPixelCount = 0;
                    int contactMinimumX = width;
                    int contactMaximumX = -1;
                    int contactMinimumY = height;
                    int contactMaximumY = -1;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int offset = (y * width + x) * 4;
                            byte blue = pixels[offset];
                            byte green = pixels[offset + 1];
                            byte red = pixels[offset + 2];
                            if (blue > 180 && green > 100 && red < 100)
                            {
                                contactPixelCount++;
                                contactMinimumX = Math.Min(contactMinimumX, x);
                                contactMaximumX = Math.Max(contactMaximumX, x);
                                contactMinimumY = Math.Min(contactMinimumY, y);
                                contactMaximumY = Math.Max(contactMaximumY, y);
                            }
                        }
                    }
                    for (int y = centerY - 12; y <= centerY + 12 && !foundContactColor; y++)
                    {
                        for (int x = centerX - 12; x <= centerX + 12; x++)
                        {
                            int offset = (y * width + x) * 4;
                            byte blue = pixels[offset];
                            byte green = pixels[offset + 1];
                            byte red = pixels[offset + 2];
                            maximumBlue = Math.Max(maximumBlue, blue);
                            maximumGreen = Math.Max(maximumGreen, green);
                            maximumRed = Math.Max(maximumRed, red);
                            if (blue != 0 || green != 0 || red != 0)
                                nonBlackPixelCount++;
                            if (blue > 180 && green > 100 && red < 100)
                            {
                                foundContactColor = true;
                                break;
                            }
                        }
                    }

                    Assert.True(foundContactColor,
                        $"Contact pixels not found at ({centerX},{centerY}); " +
                        $"max RGB=({maximumRed},{maximumGreen},{maximumBlue}), " +
                        $"non-black={nonBlackPixelCount}, contact pixels={contactPixelCount}, " +
                        $"contact bounds=({contactMinimumX},{contactMinimumY})-({contactMaximumX},{contactMaximumY}), " +
                        $"actual={visualizer.ActualWidth}x{visualizer.ActualHeight}.");
                    visualizer.ClearContacts();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The WPF render test timed out.");
            if (failure != null)
                throw failure;
        }

        private static TouchpadVisualizationFrame Frame(long timestamp, double normalizedX)
        {
            return new TouchpadVisualizationFrame(timestamp, new[]
            {
                new TouchpadContact(1, DeviceStates.Tip, normalizedX, 0.5)
            });
        }

        private sealed class FakeFrameSource : ITouchpadVisualizationFrameSource
        {
            private readonly object _syncRoot = new object();
            private EventHandler<TouchpadFrameEventArgs> _touchpadFrame;
            private int _subscriberCount;

            public int SubscriberCount => Volatile.Read(ref _subscriberCount);

            public event EventHandler<TouchpadFrameEventArgs> TouchpadFrame
            {
                add
                {
                    lock (_syncRoot)
                    {
                        _touchpadFrame += value;
                        Interlocked.Increment(ref _subscriberCount);
                    }
                }
                remove
                {
                    lock (_syncRoot)
                    {
                        _touchpadFrame -= value;
                        Interlocked.Decrement(ref _subscriberCount);
                    }
                }
            }

            public void Publish(TouchpadVisualizationFrame frame)
            {
                EventHandler<TouchpadFrameEventArgs> handler;
                lock (_syncRoot)
                    handler = _touchpadFrame;

                handler?.Invoke(this, new TouchpadFrameEventArgs(frame.Contacts,
                    frame.TimestampMilliseconds));
            }
        }
    }
}
