using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.Common.Input
{
    public sealed class TouchpadVisualizationFrame
    {
        private readonly TouchpadContact[] _contacts;

        public TouchpadVisualizationFrame(long timestampMilliseconds, IReadOnlyList<TouchpadContact> contacts)
        {
            if (contacts == null)
                throw new ArgumentNullException(nameof(contacts));

            TimestampMilliseconds = timestampMilliseconds;
            _contacts = new TouchpadContact[contacts.Count];
            for (int i = 0; i < contacts.Count; i++)
                _contacts[i] = contacts[i];
        }

        public long TimestampMilliseconds { get; }
        public IReadOnlyList<TouchpadContact> Contacts => _contacts;
    }

    public static class TouchpadVisualizationProtocol
    {
        private static readonly byte[] Handshake = { (byte)'G', (byte)'S', (byte)'T', (byte)'V', 2 };
        private const int FrameHeaderSize = sizeof(long) + sizeof(int);
        private const int ContactSize = sizeof(int) + sizeof(int) + sizeof(float) + sizeof(float) + sizeof(byte);
        public const int MaximumContactCount = 32;

        public static async ValueTask WriteHandshakeAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            await stream.WriteAsync(Handshake, cancellationToken).ConfigureAwait(false);
        }

        public static async ValueTask ReadHandshakeAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var received = new byte[Handshake.Length];
            await stream.ReadExactlyAsync(received, cancellationToken).ConfigureAwait(false);
            if (!received.AsSpan().SequenceEqual(Handshake))
                throw new InvalidDataException("The touchpad visualization stream uses an unsupported protocol.");
        }

        public static async ValueTask WriteFrameAsync(Stream stream, TouchpadVisualizationFrame frame,
            CancellationToken cancellationToken)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (frame.Contacts.Count > MaximumContactCount)
                throw new InvalidDataException("The touchpad visualization frame contains too many contacts.");

            var buffer = new byte[FrameHeaderSize + frame.Contacts.Count * ContactSize];
            Span<byte> payload = buffer;
            BinaryPrimitives.WriteInt64LittleEndian(payload, frame.TimestampMilliseconds);
            BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(sizeof(long)), frame.Contacts.Count);

            int offset = FrameHeaderSize;
            foreach (TouchpadContact contact in frame.Contacts)
            {
                ValidateCoordinate(contact.NormalizedX);
                ValidateCoordinate(contact.NormalizedY);
                ValidateConfidence(contact.Confidence);
                BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(offset), contact.ContactIdentifier);
                BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(offset + sizeof(int)), (int)contact.State);
                BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(offset + sizeof(int) * 2),
                    (float)contact.NormalizedX);
                BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(offset + sizeof(int) * 2 + sizeof(float)),
                    (float)contact.NormalizedY);
                payload[offset + ContactSize - sizeof(byte)] = (byte)contact.Confidence;
                offset += ContactSize;
            }

            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public static async ValueTask<TouchpadVisualizationFrame> ReadFrameAsync(Stream stream,
            CancellationToken cancellationToken)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var header = new byte[FrameHeaderSize];
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            long timestampMilliseconds = BinaryPrimitives.ReadInt64LittleEndian(header);
            int contactCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(sizeof(long)));
            if (contactCount < 0 || contactCount > MaximumContactCount)
                throw new InvalidDataException("The touchpad visualization frame contains an invalid contact count.");

            var contacts = new TouchpadContact[contactCount];
            if (contactCount == 0)
                return new TouchpadVisualizationFrame(timestampMilliseconds, contacts);

            var payload = new byte[contactCount * ContactSize];
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < contactCount; i++)
            {
                int offset = i * ContactSize;
                int contactIdentifier = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
                var state = (DeviceStates)BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(offset + sizeof(int)));
                float normalizedX = BinaryPrimitives.ReadSingleLittleEndian(
                    payload.AsSpan(offset + sizeof(int) * 2));
                float normalizedY = BinaryPrimitives.ReadSingleLittleEndian(
                    payload.AsSpan(offset + sizeof(int) * 2 + sizeof(float)));
                var confidence = (TouchpadContactConfidence)payload[offset + ContactSize - sizeof(byte)];
                ValidateCoordinate(normalizedX);
                ValidateCoordinate(normalizedY);
                ValidateConfidence(confidence);
                contacts[i] = new TouchpadContact(contactIdentifier, state, normalizedX, normalizedY, confidence);
            }

            return new TouchpadVisualizationFrame(timestampMilliseconds, contacts);
        }

        private static void ValidateCoordinate(double coordinate)
        {
            if (double.IsNaN(coordinate) || double.IsInfinity(coordinate) || coordinate < 0 || coordinate > 1)
                throw new InvalidDataException("The touchpad visualization frame contains an invalid coordinate.");
        }

        private static void ValidateConfidence(TouchpadContactConfidence confidence)
        {
            if (confidence != TouchpadContactConfidence.NotReported &&
                confidence != TouchpadContactConfidence.Confident &&
                confidence != TouchpadContactConfidence.LowConfidence)
            {
                throw new InvalidDataException(
                    "The touchpad visualization frame contains an invalid confidence value.");
            }
        }
    }
}
