using System;
using System.Windows;
using System.Windows.Controls;

namespace LingosolveStudio.Dialogs
{
    public partial class SliderDialog : Window
    {
        public string LabelText
        {
            set { this.label1.Content = value; }
        }

        private double prevValue;

        public delegate void HandleValueChange(object sender, ValueChangedEventArgs e);
        public event HandleValueChange ValueUpdated;

        public SliderDialog()
        {
            InitializeComponent();
        }

        private void slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this.ValueUpdated != null)
            {
                Slider bar = (Slider)sender;
                if (Math.Abs(bar.Value - prevValue) >= bar.SmallChange)
                {
                    prevValue = bar.Value;
                    this.ValueUpdated(this, new ValueChangedEventArgs(bar.Value));
                }
            }
        }

        public class ValueChangedEventArgs : EventArgs
        {
            public double NewValue { get; set; }
            public ValueChangedEventArgs(double value) : base()
            {
                this.NewValue = value;
            }
        }

        public void SetForContrast()
        {
            this.slider.Minimum = 5;
            this.slider.Value = 25;
            this.slider.TickFrequency = 10;
        }

        public void SetForGamma()
        {
            this.slider.Minimum = 0;
            this.slider.Value = 50;
            this.slider.TickFrequency = 10;
        }

        public void SetForThreshold()
        {
            this.slider.Minimum = 0;
            this.slider.Value = 50;
            this.slider.TickFrequency = 10;
        }

        private void buttonApply_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Visibility = Visibility.Hidden;
            this.Close();
        }

        private void buttonCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Hidden;
            this.Close();
        }
    }
}
