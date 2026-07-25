using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace GestureSign.Daemon.Triggers
{
    internal interface IWindowDragDiagnosticSink
    {
        bool TryWrite(WindowDragDiagnosticRecord record);
    }

    internal sealed class WindowDragDiagnosticRecord
    {
        public DateTimeOffset RecordedAtUtc { get; set; }
        public long SessionId { get; set; }
        public string Implementation { get; set; }
        public string WindowHandle { get; set; }
        public bool TargetLocked { get; set; }
        public bool ConfidenceFilteringEnabled { get; set; }
        public string CompletionReason { get; set; }
        public long DurationMilliseconds { get; set; }
        public int InputFrames { get; set; }
        public long MaximumInputGapMilliseconds { get; set; }
        public int InputGapsOverThreshold { get; set; }
        public int RawFramesBelowTwoContacts { get; set; }
        public int FilteredFramesBelowTwoContacts { get; set; }
        public int ConfidenceFilteredFramesBelowTwoContacts { get; set; }
        public int LowConfidenceFrames { get; set; }
        public int RecognizerMoves { get; set; }
        public int RecognizerPauses { get; set; }
        public int RecognizerResumes { get; set; }
        public long MaximumRecognizerMoveGapMilliseconds { get; set; }
        public bool ControllerBeginSucceeded { get; set; }
        public int ControllerUpdates { get; set; }
        public long MaximumControllerUpdateGapMilliseconds { get; set; }
        public int ControllerUpdateGapsOverThreshold { get; set; }
        public int WindowMoveRequests { get; set; }
        public long MaximumWindowMoveRequestGapMilliseconds { get; set; }
        public int WindowMoveRequestFailures { get; set; }
        public long MaximumSetWindowPosCallMicroseconds { get; set; }
        public int MaximumObservedWindowLagPixels { get; set; }
        public long MaximumObservedWindowLagMilliseconds { get; set; }
        public int ControllerFailures { get; set; }
        public string[] Samples { get; set; } = Array.Empty<string>();
    }

    internal sealed class WindowDragDiagnostics
    {
        internal const int HitchThresholdMilliseconds = 25;
        private const int WindowPositionTolerancePixels = 2;
        private const int MaximumSamples = 32;
        private static long _nextSessionId;

        private readonly IWindowDragDiagnosticSink _sink;
        private readonly List<string> _samples = new List<string>(MaximumSamples);

        private bool _active;
        private long _sessionId;
        private long _startedAt;
        private string _implementation;
        private string _windowHandle;
        private bool _targetLocked;
        private bool _confidenceFilteringEnabled;

        private bool _hasLatestFrame;
        private long _latestFrameTimestamp;
        private int _latestRawActiveContacts;
        private int _latestFilteredActiveContacts;
        private int _latestLowConfidenceContacts;

        private int _inputFrames;
        private long _lastInputTimestamp;
        private long _maximumInputGapMilliseconds;
        private int _inputGapsOverThreshold;
        private int _rawFramesBelowTwoContacts;
        private int _filteredFramesBelowTwoContacts;
        private int _confidenceFilteredFramesBelowTwoContacts;
        private int _lowConfidenceFrames;
        private bool _rawBelowTwo;
        private bool _confidenceFilteredBelowTwo;

        private int _recognizerMoves;
        private int _recognizerPauses;
        private int _recognizerResumes;
        private long _lastRecognizerMoveTimestamp;
        private long _maximumRecognizerMoveGapMilliseconds;

        private bool _controllerBeginSucceeded;
        private int _controllerUpdates;
        private long _lastControllerUpdateTimestamp;
        private long _maximumControllerUpdateGapMilliseconds;
        private int _controllerUpdateGapsOverThreshold;

        private int _windowMoveRequests;
        private long _lastWindowMoveRequestTimestamp;
        private long _maximumWindowMoveRequestGapMilliseconds;
        private int _windowMoveRequestFailures;
        private long _maximumSetWindowPosCallMicroseconds;
        private bool _hasRequestedWindowPosition;
        private int _requestedWindowLeft;
        private int _requestedWindowTop;
        private long? _windowBehindSinceTimestamp;
        private bool _windowLagSampled;
        private int _maximumObservedWindowLagPixels;
        private long _maximumObservedWindowLagMilliseconds;
        private int _controllerFailures;

        internal WindowDragDiagnostics(IWindowDragDiagnosticSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        internal bool Active => _active;

        internal void ObserveTouchpadFrame(long timestampMilliseconds,
            IReadOnlyList<TouchpadContact> rawContacts,
            IReadOnlyList<TouchpadContact> filteredContacts)
        {
            if (rawContacts == null || filteredContacts == null)
                return;

            int rawActiveContacts = 0;
            int lowConfidenceContacts = 0;
            for (int i = 0; i < rawContacts.Count; i++)
            {
                TouchpadContact contact = rawContacts[i];
                if (!contact.IsActive)
                    continue;

                rawActiveContacts++;
                if (contact.IsLowConfidence)
                    lowConfidenceContacts++;
            }

            int filteredActiveContacts = 0;
            for (int i = 0; i < filteredContacts.Count; i++)
            {
                if (filteredContacts[i].IsActive)
                    filteredActiveContacts++;
            }

            _hasLatestFrame = true;
            _latestFrameTimestamp = timestampMilliseconds;
            _latestRawActiveContacts = rawActiveContacts;
            _latestFilteredActiveContacts = filteredActiveContacts;
            _latestLowConfidenceContacts = lowConfidenceContacts;

            if (_active)
            {
                RecordInputFrame(timestampMilliseconds, rawActiveContacts,
                    filteredActiveContacts, lowConfidenceContacts);
            }
        }

        internal void Begin(long timestampMilliseconds,
            TouchpadWindowDragImplementation implementation,
            IntPtr windowHandle,
            bool targetLocked,
            bool confidenceFilteringEnabled)
        {
            if (_active)
                Complete(timestampMilliseconds, "superseded");

            ResetSession();
            _active = true;
            _sessionId = Interlocked.Increment(ref _nextSessionId);
            _startedAt = timestampMilliseconds;
            _implementation = implementation.ToString();
            _windowHandle = $"0x{windowHandle.ToInt64():X}";
            _targetLocked = targetLocked;
            _confidenceFilteringEnabled = confidenceFilteringEnabled;

            if (_hasLatestFrame)
            {
                RecordInputFrame(_latestFrameTimestamp, _latestRawActiveContacts,
                    _latestFilteredActiveContacts, _latestLowConfidenceContacts);
            }
        }

        internal void RecordInteraction(TouchpadInteractionEventType eventType, long timestampMilliseconds)
        {
            if (!_active)
                return;

            switch (eventType)
            {
                case TouchpadInteractionEventType.WindowDragMoved:
                    _recognizerMoves++;
                    RecordGap(timestampMilliseconds, ref _lastRecognizerMoveTimestamp,
                        ref _maximumRecognizerMoveGapMilliseconds, null, "recognizer-move-gap");
                    break;
                case TouchpadInteractionEventType.WindowDragPaused:
                    _recognizerPauses++;
                    AddSample(timestampMilliseconds, "recognizer-paused");
                    break;
                case TouchpadInteractionEventType.WindowDragResumed:
                    _recognizerResumes++;
                    AddSample(timestampMilliseconds, "recognizer-resumed");
                    break;
            }
        }

        internal void RecordControllerBegin(bool succeeded, long timestampMilliseconds)
        {
            if (!_active)
                return;

            _controllerBeginSucceeded = succeeded;
            if (!succeeded)
                AddSample(timestampMilliseconds, "controller-begin-failed");
        }

        internal void RecordControllerUpdate(long timestampMilliseconds)
        {
            if (!_active)
                return;

            _controllerUpdates++;
            long gap = RecordGap(timestampMilliseconds, ref _lastControllerUpdateTimestamp,
                ref _maximumControllerUpdateGapMilliseconds, null, null);
            if (gap > HitchThresholdMilliseconds)
            {
                _controllerUpdateGapsOverThreshold++;
                AddSample(timestampMilliseconds, $"controller-update-gap gapMs={gap}");
            }
        }

        internal void RecordWindowMoveRequest(long timestampMilliseconds,
            int requestedLeft,
            int requestedTop,
            bool succeeded,
            long callMicroseconds)
        {
            if (!_active)
                return;

            _windowMoveRequests++;
            RecordGap(timestampMilliseconds, ref _lastWindowMoveRequestTimestamp,
                ref _maximumWindowMoveRequestGapMilliseconds, null, "window-request-gap");
            _maximumSetWindowPosCallMicroseconds = Math.Max(
                _maximumSetWindowPosCallMicroseconds, callMicroseconds);
            if (!succeeded)
                _windowMoveRequestFailures++;

            _hasRequestedWindowPosition = succeeded;
            _requestedWindowLeft = requestedLeft;
            _requestedWindowTop = requestedTop;
        }

        internal void ObserveWindowPosition(long timestampMilliseconds, int left, int top)
        {
            if (!_active || !_hasRequestedWindowPosition)
                return;

            int lagPixels = Math.Max(Math.Abs(left - _requestedWindowLeft),
                Math.Abs(top - _requestedWindowTop));
            _maximumObservedWindowLagPixels = Math.Max(_maximumObservedWindowLagPixels, lagPixels);
            if (lagPixels <= WindowPositionTolerancePixels)
            {
                _windowBehindSinceTimestamp = null;
                _windowLagSampled = false;
                return;
            }

            if (!_windowBehindSinceTimestamp.HasValue)
                _windowBehindSinceTimestamp = timestampMilliseconds;

            long lagMilliseconds = Math.Max(0,
                timestampMilliseconds - _windowBehindSinceTimestamp.Value);
            _maximumObservedWindowLagMilliseconds = Math.Max(
                _maximumObservedWindowLagMilliseconds, lagMilliseconds);
            if (lagMilliseconds > HitchThresholdMilliseconds && !_windowLagSampled)
            {
                _windowLagSampled = true;
                AddSample(timestampMilliseconds,
                    $"window-position-lag lagMs={lagMilliseconds} lagPx={lagPixels}");
            }
        }

        internal void RecordControllerFailure(long timestampMilliseconds, string operation)
        {
            if (!_active)
                return;

            _controllerFailures++;
            AddSample(timestampMilliseconds, $"controller-failure operation={operation}");
        }

        internal void Complete(long timestampMilliseconds, string reason)
        {
            if (!_active)
                return;

            if (_windowBehindSinceTimestamp.HasValue)
            {
                _maximumObservedWindowLagMilliseconds = Math.Max(
                    _maximumObservedWindowLagMilliseconds,
                    Math.Max(0, timestampMilliseconds - _windowBehindSinceTimestamp.Value));
            }

            var record = new WindowDragDiagnosticRecord
            {
                RecordedAtUtc = DateTimeOffset.UtcNow,
                SessionId = _sessionId,
                Implementation = _implementation,
                WindowHandle = _windowHandle,
                TargetLocked = _targetLocked,
                ConfidenceFilteringEnabled = _confidenceFilteringEnabled,
                CompletionReason = reason,
                DurationMilliseconds = Math.Max(0, timestampMilliseconds - _startedAt),
                InputFrames = _inputFrames,
                MaximumInputGapMilliseconds = _maximumInputGapMilliseconds,
                InputGapsOverThreshold = _inputGapsOverThreshold,
                RawFramesBelowTwoContacts = _rawFramesBelowTwoContacts,
                FilteredFramesBelowTwoContacts = _filteredFramesBelowTwoContacts,
                ConfidenceFilteredFramesBelowTwoContacts = _confidenceFilteredFramesBelowTwoContacts,
                LowConfidenceFrames = _lowConfidenceFrames,
                RecognizerMoves = _recognizerMoves,
                RecognizerPauses = _recognizerPauses,
                RecognizerResumes = _recognizerResumes,
                MaximumRecognizerMoveGapMilliseconds = _maximumRecognizerMoveGapMilliseconds,
                ControllerBeginSucceeded = _controllerBeginSucceeded,
                ControllerUpdates = _controllerUpdates,
                MaximumControllerUpdateGapMilliseconds = _maximumControllerUpdateGapMilliseconds,
                ControllerUpdateGapsOverThreshold = _controllerUpdateGapsOverThreshold,
                WindowMoveRequests = _windowMoveRequests,
                MaximumWindowMoveRequestGapMilliseconds = _maximumWindowMoveRequestGapMilliseconds,
                WindowMoveRequestFailures = _windowMoveRequestFailures,
                MaximumSetWindowPosCallMicroseconds = _maximumSetWindowPosCallMicroseconds,
                MaximumObservedWindowLagPixels = _maximumObservedWindowLagPixels,
                MaximumObservedWindowLagMilliseconds = _maximumObservedWindowLagMilliseconds,
                ControllerFailures = _controllerFailures,
                Samples = _samples.ToArray()
            };

            _active = false;
            try
            {
                _sink.TryWrite(record);
            }
            catch
            {
                // Diagnostics must never affect the input path.
            }
        }

        private void RecordInputFrame(long timestampMilliseconds,
            int rawActiveContacts,
            int filteredActiveContacts,
            int lowConfidenceContacts)
        {
            _inputFrames++;
            long gap = RecordGap(timestampMilliseconds, ref _lastInputTimestamp,
                ref _maximumInputGapMilliseconds, null, null);
            if (gap > HitchThresholdMilliseconds)
            {
                _inputGapsOverThreshold++;
                AddSample(timestampMilliseconds, $"input-gap gapMs={gap}");
            }

            bool rawBelowTwo = rawActiveContacts < 2;
            bool confidenceFilteredBelowTwo = rawActiveContacts >= 2 && filteredActiveContacts < 2;
            if (rawBelowTwo)
                _rawFramesBelowTwoContacts++;
            if (filteredActiveContacts < 2)
                _filteredFramesBelowTwoContacts++;
            if (confidenceFilteredBelowTwo)
                _confidenceFilteredFramesBelowTwoContacts++;
            if (lowConfidenceContacts != 0)
                _lowConfidenceFrames++;

            if (rawBelowTwo && !_rawBelowTwo)
            {
                AddSample(timestampMilliseconds,
                    $"raw-contact-drop raw={rawActiveContacts} filtered={filteredActiveContacts}");
            }
            if (confidenceFilteredBelowTwo && !_confidenceFilteredBelowTwo)
            {
                AddSample(timestampMilliseconds,
                    $"confidence-contact-drop raw={rawActiveContacts} filtered={filteredActiveContacts} low={lowConfidenceContacts}");
            }

            _rawBelowTwo = rawBelowTwo;
            _confidenceFilteredBelowTwo = confidenceFilteredBelowTwo;
        }

        private long RecordGap(long timestampMilliseconds,
            ref long lastTimestamp,
            ref long maximumGap,
            Action overThreshold,
            string sampleCategory)
        {
            long gap = lastTimestamp == 0 ? 0 : Math.Max(0, timestampMilliseconds - lastTimestamp);
            lastTimestamp = timestampMilliseconds;
            maximumGap = Math.Max(maximumGap, gap);
            if (gap > HitchThresholdMilliseconds)
            {
                overThreshold?.Invoke();
                if (sampleCategory != null)
                    AddSample(timestampMilliseconds, $"{sampleCategory} gapMs={gap}");
            }
            return gap;
        }

        private void AddSample(long timestampMilliseconds, string value)
        {
            if (_samples.Count >= MaximumSamples)
                return;

            _samples.Add($"{Math.Max(0, timestampMilliseconds - _startedAt)}ms {value}");
        }

        private void ResetSession()
        {
            _samples.Clear();
            _inputFrames = 0;
            _lastInputTimestamp = 0;
            _maximumInputGapMilliseconds = 0;
            _inputGapsOverThreshold = 0;
            _rawFramesBelowTwoContacts = 0;
            _filteredFramesBelowTwoContacts = 0;
            _confidenceFilteredFramesBelowTwoContacts = 0;
            _lowConfidenceFrames = 0;
            _rawBelowTwo = false;
            _confidenceFilteredBelowTwo = false;
            _recognizerMoves = 0;
            _recognizerPauses = 0;
            _recognizerResumes = 0;
            _lastRecognizerMoveTimestamp = 0;
            _maximumRecognizerMoveGapMilliseconds = 0;
            _controllerBeginSucceeded = false;
            _controllerUpdates = 0;
            _lastControllerUpdateTimestamp = 0;
            _maximumControllerUpdateGapMilliseconds = 0;
            _controllerUpdateGapsOverThreshold = 0;
            _windowMoveRequests = 0;
            _lastWindowMoveRequestTimestamp = 0;
            _maximumWindowMoveRequestGapMilliseconds = 0;
            _windowMoveRequestFailures = 0;
            _maximumSetWindowPosCallMicroseconds = 0;
            _hasRequestedWindowPosition = false;
            _requestedWindowLeft = 0;
            _requestedWindowTop = 0;
            _windowBehindSinceTimestamp = null;
            _windowLagSampled = false;
            _maximumObservedWindowLagPixels = 0;
            _maximumObservedWindowLagMilliseconds = 0;
            _controllerFailures = 0;
        }
    }

    internal sealed class WindowDragDiagnosticWriter : IWindowDragDiagnosticSink, IDisposable
    {
        internal const string FileName = "TouchPilot-window-drag-diagnostics.jsonl";
        private const long MaximumLogSizeBytes = 4 * 1024 * 1024;
        private static readonly Lazy<WindowDragDiagnosticWriter> Shared =
            new Lazy<WindowDragDiagnosticWriter>(() => new WindowDragDiagnosticWriter(GetDefaultLogPath()));
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly BlockingCollection<WindowDragDiagnosticRecord> _records =
            new BlockingCollection<WindowDragDiagnosticRecord>(32);
        private readonly Thread _writerThread;
        private readonly string _path;
        private int _disposed;

        private WindowDragDiagnosticWriter(string path)
        {
            _path = path;
            _writerThread = new Thread(WriteRecords)
            {
                IsBackground = true,
                Name = "TouchPilot window drag diagnostic writer"
            };
            _writerThread.Start();
        }

        internal static WindowDragDiagnosticWriter Instance => Shared.Value;

        internal static string GetDefaultLogPath()
        {
            return Path.Combine(AppConfig.LocalApplicationDataPath, FileName);
        }

        internal static void ShutdownShared()
        {
            if (Shared.IsValueCreated)
                Shared.Value.Dispose();
        }

        internal static WindowDragDiagnosticWriter CreateForTest(string path)
        {
            return new WindowDragDiagnosticWriter(path);
        }

        public bool TryWrite(WindowDragDiagnosticRecord record)
        {
            if (record == null || Volatile.Read(ref _disposed) != 0 || _records.IsAddingCompleted)
                return false;

            try
            {
                return _records.TryAdd(record);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void WriteRecords()
        {
            StreamWriter writer = null;
            try
            {
                foreach (WindowDragDiagnosticRecord record in _records.GetConsumingEnumerable())
                {
                    if (writer == null || writer.BaseStream.Length >= MaximumLogSizeBytes)
                    {
                        writer?.Dispose();
                        writer = OpenWriter();
                    }

                    writer.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
                    writer.Flush();
                }
            }
            catch (Exception exception)
            {
                Logging.LogException(new InvalidOperationException(
                    "The window drag diagnostic writer failed.", exception));
            }
            finally
            {
                writer?.Dispose();
            }
        }

        private StreamWriter OpenWriter()
        {
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(_path) && new FileInfo(_path).Length >= MaximumLogSizeBytes)
                File.Move(_path, _path + ".previous", true);

            return new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _records.CompleteAdding();
            _writerThread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
