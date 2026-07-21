using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Daemon.Visualization;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
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
