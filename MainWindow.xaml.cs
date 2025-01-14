﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Screenshots.Adorner;
using CheckBox = System.Windows.Controls.CheckBox;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

namespace Screenshots
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Clipboard.Clear();

            DataContext = this;

            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                dpiX = graphics.DpiX / 96;
                dpiY = graphics.DpiY / 96;
            }

            _screen = CopyScreen();
            Canvas.Width = _screen.Width;
            Canvas.Height = _screen.Height;
            ImageMask.Source = _screen;
            Image.Source = _screen;
            Image.Visibility = Visibility.Collapsed;

            ResizeAdorner = new ResizeAdorner(AdornerRectangle);
            ResizeAdorner.ShowHandles = true;
            ResizeAdorner.OnHandleMouseDown += ResizeAdorner_OnHandleMouseDown;

            var winds = WinApi.GetWindows();
            var WS_CLIPCHILDREN = 0x02000000L;
            var WS_BORDER = 0x00800000L;
            foreach (IntPtr win in winds)
            {
                var p = WinApi.GetWindowLongPtr(win, WinApi.GWL.GWL_STYLE);

                if (((long)p & WS_CLIPCHILDREN) == WS_CLIPCHILDREN
                 || ((long)p & WS_BORDER) == WS_BORDER
                )
                {
                    WinApi.GetWindowRect(win, out WinApi.RECT rect);
                    wins.Add(rect);
                }
            }
        }



        #region 属性

        private bool IsDrawing => (rectbtn.IsChecked.HasValue && rectbtn.IsChecked.Value) ||
                                  (criclebtn.IsChecked.HasValue && criclebtn.IsChecked.Value) ||
                                  (penbtn.IsChecked.HasValue && penbtn.IsChecked.Value) ||
                                  (mosaicbtn.IsChecked.HasValue && mosaicbtn.IsChecked.Value) ||
                                  (textbtn.IsChecked.HasValue && textbtn.IsChecked.Value);
        private ResizeAdorner ResizeAdorner
        {
            get;
            set;
        }
        private bool AdornerShown
        {
            get;
            set;
        }

        private List<WinApi.RECT> wins = new List<WinApi.RECT>();

        private Handle _resizeHandle;
        private BitmapSource _screen;
        private double x;
        private double y;
        private bool _isSelected;
        private double dpiX = 1.0;
        private double dpiY = 1.0;
        /// <summary>
        /// 记录截图后的图像
        /// </summary>
        public BitmapSource _cutImage;

        /// <summary>
        /// 粗细、颜色工具条显示在截图工具条的上方
        /// </summary>
        private bool UpControlGridPostion = false;

        #endregion

        #region 窗口事件

        private void MainWindow_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelected)
            {
                CloseImage();
            }
            else
            {
                this.Close();
            }
        }

        private void MainWindow_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                var point = Mouse.GetPosition(this);
                if (Controller.TransformToVisual(this).TransformBounds(new Rect(Controller.RenderSize)).Contains(point))
                {
                    return;
                }
                if (ControlGrid.TransformToVisual(this).TransformBounds(new Rect(ControlGrid.RenderSize)).Contains(point))
                {
                    return;
                }

                CopyToClipBoard();
                this.Close();
            }

            UpdateXY();
        }

        private void MainWindow_OnMouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(ImageMask);

            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePoint = new Point(p.X, p.Y);

                if (_resizeHandle != null)
                {
                    //resize
                    ResizeImage(mousePoint, _resizeHandle);
                }
                else
                {
                    //拖动选择截图区域

                    if (!_isSelected)
                    {
                        SelectImage(mousePoint);
                    }
                    else
                    {
                        if (IsDrawing)
                        {
                            return;
                        }

                        //移动截图区域
                        MoveImage(mousePoint);
                    }
                }
            }
            else
            {
                //自动根据窗口判断截图区域(不太准确，暂时关闭)

                //if (!_isSelected)
                //{
                //    if (wins.Any(x => x.Left <= p.X && x.Top <= p.Y && x.Right >= p.X && x.Bottom >= p.Y))
                //    {
                //        var rect = wins.First(x => x.Left <= p.X && x.Top <= p.Y && x.Right >= p.X && x.Bottom >= p.Y);

                //        SetImage(new Rect(new Point(rect.Left, rect.Top), new Size(rect.Right - rect.Left, rect.Bottom - rect.Top)));
                //    }
                //}

            }
        }

        private void MainWindow_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isSelected = Image.Visibility == Visibility.Visible;

            _resizeHandle = null;

            Controller.Visibility = _isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        #endregion

        #region 装饰器

        /// <summary>
        /// 显示装饰器
        /// </summary>
        private void ShowAdorner()
        {
            if (AdornerLayer.GetAdornerLayer(AdornerRectangle) != null)
            {
                if (!AdornerShown)
                {
                    AdornerLayer.GetAdornerLayer(AdornerRectangle).Add(ResizeAdorner);
                    AdornerShown = true;
                }
                else
                {
                    RefreshAdorner();
                }
            }
        }
        /// <summary>
        /// 移除装饰器
        /// </summary>
        private void HideAdorner()
        {
            var adorner = AdornerLayer.GetAdornerLayer(AdornerRectangle);
            if (AdornerShown && adorner != null)
            {
                adorner.Remove(ResizeAdorner);
                AdornerShown = false;
            }
        }

        private void RefreshAdorner()
        {
            var adorner = AdornerLayer.GetAdornerLayer(AdornerRectangle);
            if (AdornerShown && adorner != null)
            {
                adorner.InvalidateVisual();
                ResizeAdorner.InvalidateVisual();
            }
        }
        private void ResizeAdorner_OnHandleMouseDown(Handle handle)
        {
            _resizeHandle = handle;
            UpdateXY();
        }

        #endregion

        #region 私有方法
        public BitmapSource CopyToClipBoard()
        {
            RectangleGeometry clip = Image.Clip as RectangleGeometry;
            if (clip == null) { return null; }

            var clipRect = clip.Rect;

            try
            {
                var width = (int)Math.Min(Image.ActualWidth - clipRect.X, clipRect.Width);
                var height = (int)Math.Min(Image.ActualHeight - clipRect.Y, clipRect.Height);

                width = (int)(width * dpiX);
                height = (int)(height * dpiY);
                clipRect.X *= dpiX;
                clipRect.Y *= dpiY;

                if (clipRect.X + width > _screen.Width || (clipRect.Y + height) > _screen.Height)
                {
                    clipRect.X = clipRect.Y = 0;
                    width = (int)_screen.Width;
                    height = (int)_screen.Height;
                }

                var rect = new Rect(new Size(width, height));
                
                CroppedBitmap cb = new CroppedBitmap(_screen, new Int32Rect((int)clipRect.X, (int)clipRect.Y, (int)width, (int)height));

                DrawingVisual visual = new DrawingVisual();
                DrawingContext context = visual.RenderOpen();
                context.DrawImage(cb, rect);
                context.DrawRectangle(new VisualBrush(AdornerRectangle), null, rect);
                context.Close();

                RenderTargetBitmap bmp = new RenderTargetBitmap(width, height, 96d, 96d, PixelFormats.Pbgra32);
                bmp.Render(visual);

                Clipboard.SetImage(bmp);
                return bmp;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        private void CloseImage()
        {
            Image.Visibility = Visibility.Collapsed;
            HideAdorner();

            _isSelected = false;
            Controller.Visibility = Visibility.Collapsed;
            ControlGrid.Visibility = Visibility.Collapsed;
            AdornerRectangle.Children.Clear();
            UpdateUndoBtnEnable();
            UnCheckBtn();
        }

        /// <summary>
        /// 获取屏幕截图
        /// </summary>
        /// <returns></returns>
        private BitmapSource CopyScreen()
        {
            //var left = Screen.AllScreens.Min(screen => screen.Bounds.X);
            //var top = Screen.AllScreens.Min(screen => screen.Bounds.Y);
            //var right = Screen.AllScreens.Max(screen => screen.Bounds.X + screen.Bounds.Width);
            //var bottom = Screen.AllScreens.Max(screen => screen.Bounds.Y + screen.Bounds.Height);
            //var width = right - left;
            //var height = bottom - top;

            var width = SystemParameters.PrimaryScreenWidth * dpiX;
            var height = SystemParameters.PrimaryScreenHeight * dpiY;

            //System.Windows.MessageBox.Show(width.ToString() + "====" + height.ToString());

            using (var screenBmp = new Bitmap((int)width, (int)height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var bmpGraphics = Graphics.FromImage(screenBmp))
                {
                    bmpGraphics.CopyFromScreen(0, 0, 0, 0, screenBmp.Size, CopyPixelOperation.SourceCopy);
                    return Imaging.CreateBitmapSourceFromHBitmap(
                        screenBmp.GetHbitmap(),
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
            }
        }


        /// <summary>
        /// 拖动选择图像
        /// </summary>
        private void SelectImage(Point mousePoint)
        {
            // 1. 通过一个矩形来表示目前截图区域

            double rectWidth = Math.Abs(mousePoint.X - x);
            double rectHeight = Math.Abs(mousePoint.Y - y);

            Point p = new Point(Math.Min(mousePoint.X, x), Math.Min(mousePoint.Y, y));

            SetImage(new Rect(p, new Size(rectWidth, rectHeight)));
        }

        /// <summary>
        /// 移动截图区域
        /// </summary>
        /// <param name="mousePoint"></param>
        private void MoveImage(Point mousePoint)
        {
            RectangleGeometry clip = Image.Clip as RectangleGeometry;
            if (clip == null) { return; }

            var clipRect = clip.Rect;

            if (clipRect.Contains(mousePoint))
            {
                var deltax = clipRect.X + mousePoint.X - x;
                var deltay = clipRect.Y + mousePoint.Y - y;

                bool updatex = true;
                if (deltax < 0)
                {
                    deltax = 0;
                    updatex = false;
                }

                if (deltax > Image.ActualWidth - clipRect.Size.Width)
                {
                    deltax = Image.ActualWidth - clipRect.Size.Width;
                    updatex = false;
                }

                bool updatey = true;
                if (deltay < 0)
                {
                    deltay = 0;
                    updatey = false;
                }

                if (deltay > Image.ActualHeight - clipRect.Size.Height)
                {
                    deltay = Image.ActualHeight - clipRect.Size.Height;
                    updatey = false;
                }

                SetImage(new Rect(new Point(deltax, deltay), clipRect.Size));

                UpdateXY(updatex, updatey);
            }
        }

        /// <summary>
        /// 拖动选择大小
        /// </summary>
        private void ResizeImage(Point mousePoint, Handle handle)
        {
            RectangleGeometry clip = Image.Clip as RectangleGeometry;
            if (clip == null) { return; }

            var clipRect = clip.Rect;

            double rectWidth = mousePoint.X - x;
            double rectHeight = mousePoint.Y - y;

            var rect_x = clipRect.X + rectWidth;
            var rect_y = clipRect.Y + rectHeight;
            var rect_width = Math.Max(0, clipRect.Width + rectWidth);
            var rect_height = Math.Max(0, clipRect.Height + rectHeight);

            if (handle.Location == ThumbLocation.LeftCenter || handle.Location == ThumbLocation.BottomLeft ||
                handle.Location == ThumbLocation.TopLeft)
            {
                rect_width = Math.Max(0, clipRect.Width - rectWidth);
                rect_x = Math.Min(rect_x, clipRect.Right);
            }
            if (handle.Location == ThumbLocation.TopLeft || handle.Location == ThumbLocation.TopCenter ||
                handle.Location == ThumbLocation.TopRight)
            {
                rect_height = Math.Max(0, clipRect.Height - rectHeight);
                rect_y = Math.Min(rect_y, clipRect.Bottom);
            }


            Rect rect = Rect.Empty;
            switch (handle.Location)
            {
                case ThumbLocation.BottomCenter:
                    rect = new Rect(clipRect.Location, new Size(clipRect.Width, rect_height));
                    break;
                case ThumbLocation.BottomLeft:
                    rect = new Rect(new Point(rect_x, clipRect.Location.Y), new Size(rect_width, rect_height));
                    break;
                case ThumbLocation.BottomRight:
                    rect = new Rect(clipRect.Location, new Size(rect_width, rect_height));
                    break;
                case ThumbLocation.LeftCenter:
                    rect = new Rect(new Point(rect_x, clipRect.Location.Y), new Size(rect_width, clipRect.Height));
                    break;
                case ThumbLocation.RightCenter:
                    rect = new Rect(clipRect.Location, new Size(rect_width, clipRect.Height));
                    break;
                case ThumbLocation.TopLeft:
                    rect = new Rect(new Point(rect_x, rect_y), new Size(rect_width, rect_height));
                    break;
                case ThumbLocation.TopCenter:
                    rect = new Rect(new Point(clipRect.Location.X, rect_y), new Size(clipRect.Width, rect_height));
                    break;
                case ThumbLocation.TopRight:
                    rect = new Rect(new Point(clipRect.Location.X, rect_y), new Size(rect_width, rect_height));
                    break;
            }

            if (rect != Rect.Empty)
            {
                SetImage(rect);

                UpdateXY(rect.Width > 0, rect.Height > 0);
            }
        }

        /// <summary>
        /// 设置工具条的位置
        /// </summary>
        /// <param name="rect"></param>
        private void SetImage(Rect rect)
        {
            Image.Visibility = Visibility.Visible;
            Image.Clip = new RectangleGeometry(rect);
            Canvas.SetLeft(AdornerRectangle, rect.X);
            Canvas.SetTop(AdornerRectangle, rect.Y);
            AdornerRectangle.Width = rect.Width;
            AdornerRectangle.Height = rect.Height;
            ShowAdorner();


            Canvas.SetLeft(Controller, rect.X + rect.Width - Controller.Width);
            var y = rect.Y + rect.Height + 5;
            var y1 = rect.Y + rect.Height - Controller.Height - 5;

            if (Image.ActualHeight < y + Controller.Height) //截图区域底部不足以容纳工具条
            {
                if (rect.Y < Controller.Height + 5)//截图区域的顶部不足以容纳工具条
                {
                    Canvas.SetLeft(Controller, rect.X + rect.Width - Controller.Width - 5);
                    Canvas.SetTop(Controller, y1);
                    UpControlGridPostion = true;
                }
                else
                {
                    UpControlGridPostion = false;
                    Canvas.SetTop(Controller, rect.Y - Controller.Height - 5);
                }
            }
            else//截图区域底部可以容纳工具条
            {
                UpControlGridPostion = false;
                Canvas.SetTop(Controller, y);
            }

            Controller.Visibility = Visibility.Collapsed;
            ControlGrid.Visibility = Visibility.Collapsed;
        }

        private void UpdateXY(bool updateX = true, bool updateY = true)
        {
            if (updateX)
            {
                x = Mouse.GetPosition(ImageMask).X;
            }

            if (updateY)
            {
                y = Mouse.GetPosition(ImageMask).Y;
            }
        }

        #endregion

        #region 控制面板
        private void Okbtn_OnClick(object sender, RoutedEventArgs e)
        {
            _cutImage =  CopyToClipBoard();
            this.Close();
        }
        private void Closebtn_OnClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Rectbtn_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox btn)
            {
                var check = btn.IsChecked;

                UnCheckBtn();

                btn.IsChecked = check;
                if (btn.IsChecked.HasValue && btn.IsChecked.Value)
                {
                    OpenControlGrid(btn);
                }
                else
                {
                    ControlGrid.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UnCheckBtn()
        {
            rectbtn.IsChecked = false;
            criclebtn.IsChecked = false;
            penbtn.IsChecked = false;
            mosaicbtn.IsChecked = false;
            textbtn.IsChecked = false;
            textBox = null;
        }

        private void Downloadbtn_OnClick(object sender, RoutedEventArgs e)
        {
            _cutImage = CopyToClipBoard();

            if (_cutImage != null)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "*.png|*.png";
                if (saveFileDialog.ShowDialog() is true)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(saveFileDialog.FileName))
                        {
                            using FileStream stream = new FileStream(saveFileDialog.FileName, FileMode.Create);
                            PngBitmapEncoder encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(_cutImage));
                            encoder.Save(stream);
                        }
                    }
                    catch (Exception ex) { }
                }
                this.Close();
            }
        }
        #endregion

        #region 画图

        private UIElement _selectedElement;
        private TextBox textBox;
        private Border drawingRect;
        private Ellipse drawingEllipse;
        private Polyline drawingPolyline;
        private double drawingX;
        private double drawingY;
        private bool _istextboxEditing;
        private void UpdateDrawingXY(bool updateX = true, bool updateY = true)
        {
            if (updateX)
            {
                drawingX = Mouse.GetPosition(AdornerRectangle).X;
            }

            if (updateY)
            {
                drawingY = Mouse.GetPosition(AdornerRectangle).Y;
            }
        }

        private void AdornerRectangle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            AdornerRectangle.Focus();
            if (IsDrawing)
            {
                Point mousePoint = e.GetPosition(AdornerRectangle);

                //_selectedElement = AdornerRectangle.Children.Cast<UIElement>().FirstOrDefault(x =>
                //     x.TransformToVisual(AdornerRectangle)
                //        .TransformBounds(new Rect(x.RenderSize)).Contains(mousePoint));

                if (_selectedElement != null)
                {
                    return;
                }

                if (rectbtn.IsChecked.HasValue && rectbtn.IsChecked.Value)
                {
                    InitRect();
                }
                else if (criclebtn.IsChecked.HasValue && criclebtn.IsChecked.Value)
                {
                    InitCricle();
                }
                else if (penbtn.IsChecked.HasValue && penbtn.IsChecked.Value)
                {
                    InitPen();
                }
                else if (textbtn.IsChecked.HasValue && textbtn.IsChecked.Value)
                {
                    InitTextbox(mousePoint);
                }
            }
        }
        private void AdornerRectangle_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_istextboxEditing) { return; }

            if (IsDrawing)
            {
                UpdateDrawingXY();

                //Point mousePoint = e.GetPosition(AdornerRectangle);

                //_selectedElement = AdornerRectangle.Children.Cast<UIElement>().FirstOrDefault(x =>
                //     x.TransformToVisual(AdornerRectangle)
                //        .TransformBounds(new Rect(x.RenderSize)).Contains(mousePoint));
            }
        }
        private void AdornerRectangle_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_istextboxEditing) { return; }

            if (IsDrawing && e.LeftButton == MouseButtonState.Pressed)
            {
                AdornerRectangle.CaptureMouse();

                Point mousePoint = new Point(Mouse.GetPosition(AdornerRectangle).X, Mouse.GetPosition(AdornerRectangle).Y);

                if (_selectedElement != null)
                {
                    MoveElement(_selectedElement, mousePoint);
                    return;
                }

                if (rectbtn.IsChecked.HasValue && rectbtn.IsChecked.Value)
                {
                    DrawRect(mousePoint);
                }

                if (criclebtn.IsChecked.HasValue && criclebtn.IsChecked.Value)
                {
                    DrawEllipse(mousePoint);
                }

                if (penbtn.IsChecked.HasValue && penbtn.IsChecked.Value)
                {
                    DrawPath(mousePoint);
                }
            }
        }

        private void InitRect()
        {
            drawingRect = new Border()
            {
                Cursor = Cursors.SizeAll,
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(SizeSelected / 2),
                BorderBrush = new SolidColorBrush(ColorSelected)
            };
            drawingRect.MouseLeftButtonDown += (sender, args) => { _selectedElement = (Border)sender; };
        }

        private void InitCricle()
        {
            drawingEllipse = new Ellipse
            {
                Cursor = Cursors.SizeAll,
                StrokeThickness = SizeSelected / 2,
                Stroke = new SolidColorBrush(ColorSelected)
            };
            drawingEllipse.MouseLeftButtonDown += (sender, args) => { _selectedElement = (Ellipse)sender; };
        }

        private void InitPen()
        {
            drawingPolyline = new Polyline
            {
                Cursor = Cursors.SizeAll,
                Width = AdornerRectangle.ActualWidth,
                Height = AdornerRectangle.ActualHeight,
                StrokeThickness = SizeSelected / 2,
                Stroke = new SolidColorBrush(ColorSelected)
            };
            
            drawingPolyline.MouseLeftButtonDown += (sender, args) => { _selectedElement = (Polyline)sender; };
            Canvas.SetTop(drawingPolyline, 0);
            Canvas.SetLeft(drawingPolyline, 0);
        }

        private void InitTextbox(Point mousePoint)
        {
            if (textBox == null)
            {
                textBox = new TextBox
                {
                    MinWidth = 100,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Colors.Transparent),
                    AcceptsReturn = true,
                    FontFamily = new FontFamily("Microsoft YaHei")
                };
                textBox.LostFocus += (sender, args) =>
                {
                    if (sender is TextBox box)
                    {
                        box.SelectionLength = 0;
                        box.BorderThickness = new Thickness(0);
                        box.IsReadOnly = true;
                        _istextboxEditing = false;
                        if (textBox == box && string.IsNullOrEmpty(box.Text))
                        {
                            AdornerRectangle.Children.Remove(box);
                            textBox = null;
                        }
                    }
                };
                textBox.GotFocus += (sender, args) =>
                {
                    if (sender is TextBox box)
                    {
                        box.BorderThickness = new Thickness(1);
                    }
                };
                textBox.MouseDoubleClick += (sender, args) =>
                 {
                     if (sender is TextBox box)
                     {
                         _istextboxEditing = true;
                         box.IsReadOnly = false;
                         box.Cursor = Cursors.Arrow;
                         box.Focus();
                         box.SelectionLength = 0;
                     }
                 };
                textBox.MouseEnter += (sender, args) =>
                {
                    if (sender is TextBox box)
                    {
                        box.Cursor = box.IsReadOnly ? Cursors.SizeAll : Cursors.Arrow;
                    }
                };
                textBox.PreviewMouseLeftButtonDown += (sender, args) =>
                {
                    _selectedElement = (TextBox)sender;
                };
                AdornerRectangle.Children.Add(textBox);
            }

            if (string.IsNullOrEmpty(textBox.Text))
            {
                textBox.MaxWidth = AdornerRectangle.RenderSize.Width - mousePoint.X;
                textBox.MaxHeight = AdornerRectangle.RenderSize.Height - mousePoint.Y;
                textBox.BorderBrush = new SolidColorBrush(ColorSelected);
                textBox.Foreground = new SolidColorBrush(ColorSelected);
                textBox.CaretBrush = new SolidColorBrush(ColorSelected);
                textBox.FontSize = SizeSelected * 3;

                Canvas.SetLeft(textBox, mousePoint.X);
                Canvas.SetTop(textBox, mousePoint.Y);
                textBox.Focus();
                textBox.IsReadOnly = false;
            }
            else
            {
                textBox = null;
            }
        }
        private void AdornerRectangle_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _selectedElement = null;
            drawingRect = null;
            drawingEllipse = null;
            drawingPolyline = null;
            UpdateUndoBtnEnable();
            AdornerRectangle.ReleaseMouseCapture();
        }

        private void DrawRect(Point mousePoint)
        {
            if (drawingRect != null)
            {
                var x = Math.Min(Math.Max(0, mousePoint.X), AdornerRectangle.RenderSize.Width);
                var y = Math.Min(Math.Max(0, mousePoint.Y), AdornerRectangle.RenderSize.Height);

                double rectWidth = Math.Abs(x - drawingX);
                double rectHeight = Math.Abs(y - drawingY);

                Point p = new Point(Math.Min(drawingX, x), Math.Min(drawingY, y));

                Canvas.SetLeft(drawingRect, p.X);
                Canvas.SetTop(drawingRect, p.Y);
                drawingRect.Width = rectWidth;
                drawingRect.Height = rectHeight;

                if (!AdornerRectangle.Children.Contains(drawingRect))
                {
                    AdornerRectangle.Children.Add(drawingRect);
                }
            }
        }

        private void DrawEllipse(Point mousePoint)
        {
            if (drawingEllipse != null)
            {
                var x = Math.Min(Math.Max(0, mousePoint.X), AdornerRectangle.RenderSize.Width);
                var y = Math.Min(Math.Max(0, mousePoint.Y), AdornerRectangle.RenderSize.Height);

                double rectWidth = Math.Abs(x - drawingX);
                double rectHeight = Math.Abs(y - drawingY);

                Point p = new Point(Math.Min(drawingX, x), Math.Min(drawingY, y));

                Canvas.SetLeft(drawingEllipse, p.X);
                Canvas.SetTop(drawingEllipse, p.Y);
                drawingEllipse.Width = rectWidth;
                drawingEllipse.Height = rectHeight;

                if (!AdornerRectangle.Children.Contains(drawingEllipse))
                {
                    AdornerRectangle.Children.Add(drawingEllipse);
                }
            }
        }

        private void DrawPath(Point mousePoint)
        {
            if (drawingPolyline != null)
            {
                drawingPolyline.Points.Add(mousePoint);

                if (!AdornerRectangle.Children.Contains(drawingPolyline))
                {
                    AdornerRectangle.Children.Add(drawingPolyline);
                }
            }
        }

        private void Undobtn_OnClick(object sender, RoutedEventArgs e)
        {
            if (AdornerRectangle.Children.Count > 0)
            {
                AdornerRectangle.Children.RemoveAt(AdornerRectangle.Children.Count - 1);
            }

            UpdateUndoBtnEnable();
        }

        private void MoveElement(UIElement element, Point mousePoint)
        {
            if (element != null)
            {
                var deltax = mousePoint.X - drawingX;
                var deltay = mousePoint.Y - drawingY;

                var left = Canvas.GetLeft(element);
                var top = Canvas.GetTop(element);

                var x = Math.Min(Math.Max(0, left + deltax),
                    AdornerRectangle.RenderSize.Width - element.RenderSize.Width);
                var y = Math.Min(Math.Max(0, top + deltay),
                    AdornerRectangle.RenderSize.Height - element.RenderSize.Height);

                if (element is Polyline polyline)
                {
                    Canvas.SetLeft(element, left + deltax);
                    Canvas.SetTop(element, top + deltay);
                    UpdateDrawingXY();
                }
                else
                {
                    Canvas.SetLeft(element, x);
                    Canvas.SetTop(element, y);
                    var updatex = (left + deltax) > 0 && (left + deltax + element.RenderSize.Width) < AdornerRectangle.RenderSize.Width;
                    var updatey = (top + deltay) > 0 && (top + deltay + element.RenderSize.Height) < AdornerRectangle.RenderSize.Height;
                    UpdateDrawingXY(updatex, updatey);
                }
            }
        }
        private void UpdateUndoBtnEnable()
        {
            undobtn.IsEnabled = AdornerRectangle.Children.Count > 0;
        }
        #endregion

        #region 颜色面板

        public List<Color> ColorList { get; set; } = new List<Color>() { Colors.Red, Colors.Orange, Colors.DodgerBlue, Colors.LawnGreen, Colors.DimGray, Colors.WhiteSmoke };
        public Color ColorSelected { get; set; } = Colors.Red;

        public List<int> SizeList { get; set; } = new List<int>() { 6, 12, 18 };
        public int SizeSelected { get; set; } = 6;

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register("Data", typeof(Geometry), typeof(MainWindow));
        public Geometry Data
        {
            get
            {
                return (Geometry)GetValue(DataProperty);
            }
            set
            {
                SetValue(DataProperty, value);
            }
        }

        private void OpenControlGrid(UIElement sender)
        {
            ControlGrid.Visibility = Visibility.Visible;
            ControlGrid.UpdateLayout();

            var element = sender.TransformToVisual(this).TransformBounds(new Rect(sender.RenderSize));
            var content = ControlGrid.TransformToVisual(this).TransformBounds(new Rect(ControlGrid.RenderSize));

            var maxX = this.RenderSize.Width - content.Width;
            var x = Convert.ToSingle(Math.Max(0, Math.Min(element.X, maxX)));

            Canvas.SetLeft(ControlGrid, x);

            if (UpControlGridPostion)
            {
                Canvas.SetTop(ControlGrid, element.Top - Controller.Height);
                UpdateArrow(element, new Rect(new Point(x, element.Top - Controller.Height), ControlGrid.RenderSize));
            }
            else
            {
                Canvas.SetTop(ControlGrid, element.Bottom);
                UpdateArrow(element, new Rect(new Point(x, element.Bottom), ControlGrid.RenderSize));
            }
        }

        private void UpdateArrow(Rect elementRect, Rect contentRect)
        {
            Size arrowSize = new Size(4, 3);

            if (contentRect.Contains(new Point(elementRect.Center().X, contentRect.Center().Y)))
            {
                var min = arrowSize.Width * 4;
                var center = Math.Max(Math.Min(elementRect.Center().X, contentRect.Right - min) - contentRect.Left, min);
                var x1 = center - arrowSize.Width;
                var x2 = center + arrowSize.Width;
                double top, bottom;

                if (elementRect.Top < contentRect.Top)
                {
                    //高亮 Element 位于上方
                    top = arrowSize.Height - arrowSize.Width;
                    bottom = arrowSize.Height;
                }
                else
                {
                    //高亮 Element 位于下方
                    top = contentRect.Size.Height - (arrowSize.Height - arrowSize.Width);
                    bottom = contentRect.Size.Height - arrowSize.Height;
                }

                SetValue(DataProperty, Geometry.Parse($"M{x1},{bottom} L{center},{top} L{x2},{bottom} L{x1},{bottom}"));
            }
            else if (contentRect.Contains(new Point(contentRect.Center().X, elementRect.Center().Y)))
            {
                var min = arrowSize.Width * 4;
                var center = Math.Max(Math.Min(elementRect.Center().Y, contentRect.Bottom - min) - contentRect.Top, min);

                var y1 = center - arrowSize.Width;
                var y2 = center + arrowSize.Width;
                double left, right;

                if (elementRect.Left < contentRect.Left)
                {
                    //高亮 Element 位于左方
                    left = arrowSize.Height - arrowSize.Width;
                    right = arrowSize.Height;
                }
                else
                {
                    //高亮 Element 位于右方
                    left = contentRect.Size.Width - (arrowSize.Height - arrowSize.Width);
                    right = contentRect.Size.Width - arrowSize.Height;
                }

                SetValue(DataProperty, Geometry.Parse($"M{right},{y1} L{left},{center} L{right},{y2} L{right},{y1}"));
            }
        }
        #endregion
    }
}
