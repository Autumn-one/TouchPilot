using GestureSign.Common;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Log;
using GestureSign.Daemon.Input;
using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GestureSign.Daemon.Visualization
{
    internal interface ITouchpadVisualizationFrameSource
    {
        event EventHandler<TouchpadFrameEventArgs> TouchpadFrame;
    }

    internal sealed class PointCaptureVisualizationFrameSource : ITouchpadVisualizationFrameSource
    {
        public event EventHandler<TouchpadFrameEventArgs> TouchpadFrame
        {
            add => PointCapture.Instance.TouchpadFrame += value;
            remove => PointCapture.Instance.TouchpadFrame -= value;
        }
    }

    internal sealed class LatestTouchpadFrameBuffer
    {
        private readonly Channel<TouchpadVisualizationFrame> _channel =
            Channel.CreateBounded<TouchpadVisualizationFrame>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

        public bool Publish(TouchpadVisualizationFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            return _channel.Writer.TryWrite(frame);
        }

        public ValueTask<TouchpadVisualizationFrame> ReadAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAsync(cancellationToken);
        }
    }

    internal sealed class TouchpadVisualizationServer : IDisposable
    {
        private readonly ITouchpadVisualizationFrameSource _frameSource;
        private readonly string _pipeName;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private Task _serverTask;
        private bool _failureLogged;
        private bool _disposed;

        public TouchpadVisualizationServer(ITouchpadVisualizationFrameSource frameSource,
            string pipeName = Constants.TouchpadVisualizationPipe)
        {
            _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
            _pipeName = string.IsNullOrWhiteSpace(pipeName)
                ? throw new ArgumentException("A pipe name is required.", nameof(pipeName))
                : pipeName;
        }

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TouchpadVisualizationServer));
            if (_serverTask != null)
                return;

            _serverTask = Task.Run(() => RunAsync(_shutdown.Token));
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (NamedPipeServerStream pipe = CreatePipe())
                    {
                        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                        _failureLogged = false;
                        await ServeClientAsync(pipe, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                    // A visualization client can disappear at any point.
                }
                catch (Exception exception)
                {
                    if (!_failureLogged)
                    {
                        Logging.LogException(exception);
                        _failureLogged = true;
                    }

                    try
                    {
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        private NamedPipeServerStream CreatePipe()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier user = identity.User ??
                                      throw new InvalidOperationException("The current Windows user has no SID.");
            var security = new PipeSecurity();
            security.SetAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                NamedPipe.GetUserPipeName(_pipeName),
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                4096,
                4096,
                security,
                HandleInheritability.None,
                (PipeAccessRights)0);
        }

        private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken shutdownToken)
        {
            await TouchpadVisualizationProtocol.WriteHandshakeAsync(pipe, shutdownToken).ConfigureAwait(false);
            await TouchpadVisualizationProtocol.WriteFrameAsync(pipe,
                new TouchpadVisualizationFrame(Environment.TickCount64, Array.Empty<TouchpadContact>()),
                shutdownToken).ConfigureAwait(false);

            using var connection = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            var frames = new LatestTouchpadFrameBuffer();
            EventHandler<TouchpadFrameEventArgs> frameHandler = (sender, eventArgs) =>
            {
                frames.Publish(new TouchpadVisualizationFrame(eventArgs.TimestampMilliseconds,
                    eventArgs.Contacts));
            };

            _frameSource.TouchpadFrame += frameHandler;
            Task disconnectTask = MonitorDisconnectAsync(pipe, connection);
            try
            {
                while (!connection.IsCancellationRequested)
                {
                    TouchpadVisualizationFrame frame = await frames.ReadAsync(connection.Token)
                        .ConfigureAwait(false);
                    await TouchpadVisualizationProtocol.WriteFrameAsync(pipe, frame, connection.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (connection.IsCancellationRequested)
            {
            }
            finally
            {
                _frameSource.TouchpadFrame -= frameHandler;
                connection.Cancel();
                try
                {
                    await disconnectTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException)
                {
                }
            }
        }

        private static async Task MonitorDisconnectAsync(NamedPipeServerStream pipe,
            CancellationTokenSource connection)
        {
            var buffer = new byte[1];
            try
            {
                while (await pipe.ReadAsync(buffer, connection.Token).ConfigureAwait(false) != 0)
                {
                }
            }
            finally
            {
                connection.Cancel();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _shutdown.Cancel();
            bool serverStopped = _serverTask == null;
            try
            {
                serverStopped = _serverTask == null || _serverTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException exception) when (exception.InnerException is OperationCanceledException)
            {
                serverStopped = true;
            }
            finally
            {
                if (serverStopped)
                    _shutdown.Dispose();
            }
        }
    }
}
