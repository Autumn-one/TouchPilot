using GestureSign.Common;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Log;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.ControlPanel.Visualization
{
    internal sealed class TouchpadVisualizationClient : IDisposable
    {
        private readonly string _pipeName;
        private readonly TimeSpan _reconnectDelay;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly object _pipeLock = new object();
        private NamedPipeClientStream _activePipe;
        private Task _readerTask;
        private int _connected;
        private bool _failureLogged;
        private bool _disposed;

        public TouchpadVisualizationClient(string pipeName = Constants.TouchpadVisualizationPipe,
            TimeSpan? reconnectDelay = null)
        {
            _pipeName = string.IsNullOrWhiteSpace(pipeName)
                ? throw new ArgumentException("A pipe name is required.", nameof(pipeName))
                : pipeName;
            _reconnectDelay = reconnectDelay ?? TimeSpan.FromMilliseconds(300);
        }

        public event Action<TouchpadVisualizationClient, TouchpadVisualizationFrame> FrameReceived;
        public event Action<TouchpadVisualizationClient, bool> ConnectionChanged;

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TouchpadVisualizationClient));
            if (_readerTask != null)
                return;

            _readerTask = Task.Run(() => RunAsync(_shutdown.Token));
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var pipe = new NamedPipeClientStream(".", NamedPipe.GetUserPipeName(_pipeName),
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                SetActivePipe(pipe);
                try
                {
                    await pipe.ConnectAsync(1000, cancellationToken).ConfigureAwait(false);
                    await TouchpadVisualizationProtocol.ReadHandshakeAsync(pipe, cancellationToken)
                        .ConfigureAwait(false);
                    _failureLogged = false;
                    SetConnected(true);
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        TouchpadVisualizationFrame frame = await TouchpadVisualizationProtocol
                            .ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false);
                        FrameReceived?.Invoke(this, frame);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                }
                catch (TimeoutException)
                {
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (!_failureLogged)
                    {
                        Logging.LogException(exception);
                        _failureLogged = true;
                    }
                }
                finally
                {
                    SetConnected(false);
                    ClearActivePipe(pipe);
                }

                try
                {
                    await Task.Delay(_reconnectDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private void SetActivePipe(NamedPipeClientStream pipe)
        {
            lock (_pipeLock)
                _activePipe = pipe;
        }

        private void ClearActivePipe(NamedPipeClientStream pipe)
        {
            lock (_pipeLock)
            {
                if (ReferenceEquals(_activePipe, pipe))
                    _activePipe = null;
            }
        }

        private void SetConnected(bool connected)
        {
            int value = connected ? 1 : 0;
            if (Interlocked.Exchange(ref _connected, value) != value)
                ConnectionChanged?.Invoke(this, connected);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _shutdown.Cancel();
            lock (_pipeLock)
                _activePipe?.Dispose();

            Task readerTask = _readerTask;
            if (readerTask == null)
            {
                _shutdown.Dispose();
                return;
            }

            readerTask.ContinueWith(task =>
            {
                _ = task.Exception;
                _shutdown.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
