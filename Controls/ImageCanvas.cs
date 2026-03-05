using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LingosolveStudio.Controls
{
    public class ImageCanvas : Canvas
    {
        private Point? dragStartPoint = null;
        private Point startPoint;
        private ContentControl canvasContent;

        private Dictionary<SolidColorBrush, List<System.Drawing.Rectangle>> map;

        public Dictionary<SolidColorBrush, List<System.Drawing.Rectangle>> SegmentedRegions
        {
            get { return map; }
            set
            {
                map = value;
                if (this.Children.Contains(canvasContent))
                {
                    this.Children.Clear();
                    this.Children.Add(canvasContent);
                }
                else
                {
                    this.Children.Clear();
                }
                DrawRegions(map);
            }
        }

        void DrawRegions(Dictionary<SolidColorBrush, List<System.Drawing.Rectangle>> map)
        {
            if (map != null)
            {
                foreach (SolidColorBrush color in map.Keys)
                {
                    foreach (System.Drawing.Rectangle reg in map[color])
                    {
                        Rectangle rect = new Rectangle();
                        rect.Stroke = color;
                        Canvas.SetLeft(rect, reg.X);
                        Canvas.SetTop(rect, reg.Y);
                        rect.Width = reg.Width;
                        rect.Height = reg.Height;
                        this.Children.Add(rect);
                    }
                }
            }
        }

        public System.Drawing.Rectangle GetROI()
        {
            if (canvasContent == null || canvasContent.Width == 0 || canvasContent.Height == 0)
            {
                return System.Drawing.Rectangle.Empty;
            }
            return new System.Drawing.Rectangle((int)Canvas.GetLeft(canvasContent), (int)Canvas.GetTop(canvasContent), (int)canvasContent.Width, (int)canvasContent.Height);
        }

        public void Deselect()
        {
            dragStartPoint = null;
            this.Children.Remove(canvasContent);
            canvasContent.Width = 0;
            canvasContent.Height = 0;
            canvasContent.Visibility = Visibility.Hidden;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            canvasContent = (ContentControl)this.FindName("canvasContent");
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            if (!this.Children.Contains(canvasContent))
            {
                this.Children.Add(canvasContent);
            }

            startPoint = e.GetPosition(this);
            if (canvasContent.InputHitTest(startPoint) == null)
            {
                canvasContent.Width = 0;
                canvasContent.Height = 0;
                Canvas.SetLeft(canvasContent, startPoint.X);
                Canvas.SetTop(canvasContent, startPoint.Y);
                canvasContent.Visibility = Visibility.Hidden;
                dragStartPoint = startPoint;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                this.dragStartPoint = null;
            }

            if (e.LeftButton == MouseButtonState.Released || !this.dragStartPoint.HasValue)
                return;

            var pos = e.GetPosition(this);
            var x = Math.Min(pos.X, startPoint.X);
            var y = Math.Min(pos.Y, startPoint.Y);
            var w = Math.Max(pos.X, startPoint.X) - x;
            var h = Math.Max(pos.Y, startPoint.Y) - y;
            canvasContent.Width = w;
            canvasContent.Height = h;
            Canvas.SetLeft(canvasContent, x);
            Canvas.SetTop(canvasContent, y);
            canvasContent.Visibility = Visibility.Visible;

            if (this.dragStartPoint.HasValue)
            {
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            this.dragStartPoint = null;
        }
    }
}
