using GestureSign.Common.Input;
using GestureSign.Daemon.Triggers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace GestureSign.Tests
{
    public class WindowDragDiagnosticsTests
    {
        [Fact]
        public void SessionSummaryDistinguishesInputFilteringControllerGapsAndWindowLag()
        {
            var sink = new CapturingSink();
            var diagnostics = new WindowDragDiagnostics(sink);
            TouchpadContact moving = Contact(1, TouchpadContactConfidence.Confident);
            TouchpadContact anchor = Contact(2, TouchpadContactConfidence.Confident);

            diagnostics.ObserveTouchpadFrame(100, Frame(moving, anchor), Frame(moving, anchor), 30);
            diagnostics.Begin(100, TouchpadWindowDragImplementation.DirectSetWindowPos,
                new IntPtr(123), true, true);
            diagnostics.RecordControllerBegin(true, 100);
            diagnostics.RecordControllerBeginStage(100, "prepare-window", 40_000);
            diagnostics.RecordControllerBeginDuration(100, 45_000);
            diagnostics.RecordInteraction(TouchpadInteractionEventType.WindowDragMoved, 100);
            diagnostics.RecordControllerUpdate(100);
            diagnostics.RecordControllerUpdateDuration(100, 35_000);
            diagnostics.RecordWindowMoveRequest(100, 300, 200, true, 45);
            diagnostics.ObserveWindowPosition(108, 280, 200);

            TouchpadContact lowConfidenceAnchor = Contact(2, TouchpadContactConfidence.LowConfidence);
            diagnostics.ObserveTouchpadFrame(108, Frame(moving, lowConfidenceAnchor), Frame(moving));
            diagnostics.RecordInteraction(TouchpadInteractionEventType.WindowDragPaused, 108);
            diagnostics.ObserveTouchpadFrame(140, Frame(moving, anchor), Frame(moving, anchor));
            diagnostics.RecordInteraction(TouchpadInteractionEventType.WindowDragResumed, 140);
            diagnostics.RecordInteraction(TouchpadInteractionEventType.WindowDragMoved, 140);
            diagnostics.RecordControllerUpdate(140);
            diagnostics.RecordInputHandlerDuration(140, 30_000);
            diagnostics.ObserveWindowPosition(140, 280, 200);
            diagnostics.Complete(150, "ended");

            WindowDragDiagnosticRecord record = Assert.Single(sink.Records);
            Assert.Equal("DirectSetWindowPos", record.Implementation);
            Assert.Equal("0x7B", record.WindowHandle);
            Assert.True(record.TargetLocked);
            Assert.True(record.ControllerBeginSucceeded);
            Assert.Equal(3, record.InputFrames);
            Assert.Equal(32, record.MaximumInputGapMilliseconds);
            Assert.Equal(1, record.InputGapsOverThreshold);
            Assert.Equal(30, record.MaximumInputDispatchDelayMilliseconds);
            Assert.Equal(1, record.InputDispatchDelaysOverThreshold);
            Assert.Equal(30_000, record.MaximumInputHandlerDurationMicroseconds);
            Assert.Equal(1, record.InputHandlerDurationsOverThreshold);
            Assert.Equal(1, record.ConfidenceFilteredFramesBelowTwoContacts);
            Assert.Equal(1, record.LowConfidenceFrames);
            Assert.Equal(1, record.RecognizerPauses);
            Assert.Equal(1, record.RecognizerResumes);
            Assert.Equal(40, record.MaximumRecognizerMoveGapMilliseconds);
            Assert.Equal(45_000, record.ControllerBeginDurationMicroseconds);
            Assert.Equal(40_000,
                record.ControllerBeginStagesMicroseconds["prepare-window"]);
            Assert.Equal(40, record.MaximumControllerUpdateGapMilliseconds);
            Assert.Equal(1, record.ControllerUpdateGapsOverThreshold);
            Assert.Equal(35_000, record.MaximumControllerUpdateDurationMicroseconds);
            Assert.Equal(1, record.ControllerUpdateDurationsOverThreshold);
            Assert.Equal(1, record.WindowMoveRequests);
            Assert.Equal(45, record.MaximumSetWindowPosCallMicroseconds);
            Assert.Equal(20, record.MaximumObservedWindowLagPixels);
            Assert.Equal(42, record.MaximumObservedWindowLagMilliseconds);
            Assert.Contains(record.Samples, sample => sample.Contains("confidence-contact-drop"));
            Assert.Contains(record.Samples, sample => sample.Contains("window-position-lag"));
        }

        [Fact]
        public void CompletionWithoutActiveSessionDoesNotWrite()
        {
            var sink = new CapturingSink();
            var diagnostics = new WindowDragDiagnostics(sink);

            diagnostics.Complete(100, "not-started");

            Assert.Empty(sink.Records);
        }

        [Fact]
        public void DedicatedWriterFlushesJsonLineOnDispose()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "TouchPilot-window-drag-diagnostics-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "diagnostics.jsonl");
            Directory.CreateDirectory(directory);

            try
            {
                using (WindowDragDiagnosticWriter writer = WindowDragDiagnosticWriter.CreateForTest(path))
                {
                    Assert.True(writer.TryWrite(new WindowDragDiagnosticRecord
                    {
                        RecordedAtUtc = DateTimeOffset.UtcNow,
                        SessionId = 42,
                        Implementation = "DirectSetWindowPos",
                        CompletionReason = "test"
                    }));
                }

                string line = Assert.Single(File.ReadAllLines(path));
                using JsonDocument document = JsonDocument.Parse(line);
                Assert.Equal(42, document.RootElement.GetProperty("sessionId").GetInt64());
                Assert.Equal("test", document.RootElement.GetProperty("completionReason").GetString());
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        private static TouchpadContact Contact(int id, TouchpadContactConfidence confidence)
        {
            return new TouchpadContact(id, DeviceStates.Tip, 0.5, 0.5, confidence);
        }

        private static IReadOnlyList<TouchpadContact> Frame(params TouchpadContact[] contacts)
        {
            return contacts;
        }

        private sealed class CapturingSink : IWindowDragDiagnosticSink
        {
            internal List<WindowDragDiagnosticRecord> Records { get; } =
                new List<WindowDragDiagnosticRecord>();

            public bool TryWrite(WindowDragDiagnosticRecord record)
            {
                Records.Add(record);
                return true;
            }
        }
    }
}
