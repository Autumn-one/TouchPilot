using GestureSign.Common;
using GestureSign.Common.Gestures;
using GestureSign.Common.InterProcessCommunication;
using System.Windows;
using System.Windows.Controls;

namespace GestureSign.ControlPanel.UserControls
{
    /// <summary>
    /// Interaction logic for GestureSelector.xaml
    /// </summary>
    public partial class GestureSelector : UserControl
    {
        public IGesture CurrentGesture
        {
            get { return (IGesture)GetValue(CurrentGestureProperty); }
            set { SetValue(CurrentGestureProperty, value); }
        }

        public static readonly DependencyProperty CurrentGestureProperty =
            DependencyProperty.Register(nameof(CurrentGesture), typeof(IGesture), typeof(GestureSelector), new PropertyMetadata(new Gesture()));

        public IGesture OldGesture { get; set; }

        public GestureSelector()
        {
            InitializeComponent();
        }

        private void MessageProcessor_GotNewPattern(object sender, PointPattern[] newPattern)
        {
            var existingSimilarGestureName = GestureManager.Instance.GetMostSimilarGestureName(newPattern);
            if (existingSimilarGestureName == null)
            {
                CurrentGesture = new Gesture(null, newPattern);
                ExistingTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (OldGesture?.Name == existingSimilarGestureName)
                {
                    CurrentGesture = new Gesture(existingSimilarGestureName, newPattern);
                }
                else
                {
                    ExistingTextBlock.Visibility = Visibility.Visible;
                    CurrentGesture = GestureManager.Instance.GetNewestGestureSample(existingSimilarGestureName);
                }
            }
            SetTrainingState(false);
        }

        private void SetTrainingState(bool state)
        {
            if (state)
            {
                CurrentGesture = null;
                DrawGestureTextBlock.Visibility = Visibility.Visible;
                ExistingTextBlock.Visibility = RedrawButton.Visibility = Visibility.Collapsed;
                MessageProcessor.GotNewPattern += MessageProcessor_GotNewPattern;
                NamedPipe.SendMessageAsync(IpcCommands.StartTeaching, Constants.Daemon);
            }
            else
            {
                DrawGestureTextBlock.Visibility = Visibility.Collapsed;
                RedrawButton.Visibility = Visibility.Visible;
                MessageProcessor.GotNewPattern -= MessageProcessor_GotNewPattern;
                NamedPipe.SendMessageAsync(IpcCommands.StopTraining, Constants.Daemon);
            }
        }

        private void RedrawButton_Click(object sender, RoutedEventArgs e)
        {
            SetTrainingState(true);
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (CurrentGesture?.PointPatterns == null)
            {
                SetTrainingState(true);
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            SetTrainingState(false);
        }
    }
}
