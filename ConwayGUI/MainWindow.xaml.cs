using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks; // Added for Parallel.For

namespace ConwayGUI
{
    public class NativeAddLibrary : IDisposable
    {
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ComputeGenerationDelegate(
            byte[] src,
            byte[] dst,
            int rowsize,
            int start_row,
            int end_row,
            int total_height
        );

        private ComputeGenerationDelegate _computeGeneration;
        private IntPtr _dllHandle = IntPtr.Zero;

        public bool Load(string dllPath)
        {
            _dllHandle = LoadLibrary(dllPath);
            if (_dllHandle == IntPtr.Zero) return false;

            IntPtr compPtr = GetProcAddress(_dllHandle, "computeGeneration");
            if (compPtr == IntPtr.Zero) return false;

            _computeGeneration = Marshal.GetDelegateForFunctionPointer<ComputeGenerationDelegate>(compPtr);
            return true;
        }

        public int ComputeGeneration(byte[] src, byte[] dst, int rowsize, int start_row, int end_row, int total_height)
        {
            if (_computeGeneration == null)
                throw new InvalidOperationException("computeGeneration pointer is NULL!");

            return _computeGeneration(src, dst, rowsize, start_row, end_row, total_height);
        }

        public void Unload()
        {
            if (_dllHandle != IntPtr.Zero)
            {
                FreeLibrary(_dllHandle);
                _dllHandle = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            Unload();
        }
    }

    public class BitmapHelper
    {
        public static BitmapSource ToWpfBitmap(Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze();
                return result;
            }
        }
    }

    public partial class MainWindow : Window
    {
        bool radioasmchecked = false;
        bool radiocppchecked = false;
        bool radiocmpchecked = false;

        Bitmap first_gen;
        int bitmap_length = 0;
        int bitmap_height = 0;
        int thread_count = 0;

        [DllImport(@"JAAsm.dll", CallingConvention = CallingConvention.Cdecl)]
        unsafe static extern int MyProc1([In, Out] byte[] src, [In, Out] byte[] dst, int rowsize, int start_row, int end_row, int total_height);

        private readonly NativeAddLibrary _lib = new NativeAddLibrary();

        public void initTestDll()
        {
            if (!_lib.Load("JACpp.dll"))
                throw new Exception("Could not load JACpp.dll");
        }

        public MainWindow()
        {
            InitializeComponent();
            var cpu_count = Environment.ProcessorCount;
            label_threads.Content = cpu_count.ToString();
            textbox_threads.Text = cpu_count.ToString();

            radiocpp.IsChecked = true;
            radiocppchecked = true;
        }

        private Bitmap ConvertTo8bpp(Bitmap src)
        {
            int w = src.Width;
            int h = src.Height;

            // Create new 8bpp bitmap
            Bitmap dst = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);

            // 1. SETUP PALETTE: Map index 0 to Black, index 1 to White
            System.Drawing.Imaging.ColorPalette palette = dst.Palette;
            palette.Entries[0] = System.Drawing.Color.Black;
            palette.Entries[1] = System.Drawing.Color.White;
            dst.Palette = palette;

            // 2. ROBUST DATA COPY (Works for ANY input format)
            System.Drawing.Imaging.BitmapData dstData = dst.LockBits(
                new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                dst.PixelFormat);

            unsafe
            {
                byte* pDst = (byte*)dstData.Scan0;

                // We use GetPixel for safety (it's slow but runs only once at startup)
                // This handles 24bpp, 32bpp, PNG, etc. correctly.
                for (int y = 0; y < h; y++)
                {
                    byte* row = pDst + (y * dstData.Stride);

                    for (int x = 0; x < w; x++)
                    {
                        // Read color from source
                        System.Drawing.Color c = src.GetPixel(x, y);

                        // Decide: Is it alive? (Brightness > 128)
                        // If White (255,255,255) -> 1
                        // If Black (0,0,0) -> 0
                        byte val = (byte)(c.GetBrightness() > 0.5f ? 1 : 0);

                        // Write 0 or 1 directly
                        row[x] = val;
                    }
                }
            }

            dst.UnlockBits(dstData);
            return dst;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Bitmap original = new Bitmap("first_generation.bmp");
                first_gen = ConvertTo8bpp(original);
                original.Dispose();
            }
            catch (Exception exception)
            {
                
                Console.WriteLine(exception.Message);

                
                AppendTextThreadSafe($"Error opening file: {exception.Message}", false);
                return;
            }

            System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, first_gen.Width, first_gen.Height);
            System.Drawing.Imaging.BitmapData bmpData =
                first_gen.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                first_gen.PixelFormat);

            IntPtr ptr = bmpData.Scan0;

            int bytes = Math.Abs(bmpData.Stride) * first_gen.Height;
            byte[] rgbValues = new byte[bytes];

            bitmap_length = rgbValues.Length;
            bitmap_height = first_gen.Height;

            try
            {
                thread_count = int.Parse(textbox_threads.Text);
            }
            catch
            {
                thread_count = 1;
            }

            int generations = 1;
            try
            {
                generations = int.Parse(textboxgenerations.Text);
            }
            catch
            {
                generations = 1;
            }

            System.Runtime.InteropServices.Marshal.Copy(ptr, rgbValues, 0, bytes);

            if (thread_count == 0) thread_count = 1;

            int rowsize = Math.Abs(bmpData.Stride);
            int total_rows = first_gen.Height;
            int rows_per_thread = total_rows / thread_count;

            byte[] buffer1 = rgbValues;
            byte[] buffer2 = new byte[bytes];
            byte[] currentBuffer = buffer1;
            byte[] nextBuffer = buffer2;

            if (radiocppchecked)
            {
                initTestDll();
                Stopwatch sw = Stopwatch.StartNew();

                for (int g = 0; g < generations; g++)
                {
                    // Use ThreadPool via Parallel.For for maximum speed
                    Parallel.For(0, thread_count, i =>
                    {
                        int start_row = i * rows_per_thread;
                        int end_row = 1;
                        
                        int safe_start = 1;
                        int safe_end = 1;

                        if (i == thread_count - 1)
                        {
                            end_row = total_rows;
                        }
                        else { 
                            end_row = start_row + rows_per_thread;
                        }

                        if (start_row == 0)
                        {
                            safe_start = 1;
                        }
                        else {
                            safe_start = start_row;
                        }

                        if (end_row == total_rows)
                        {
                            safe_end = total_rows - 1;
                        }
                        else { 
                            safe_end = end_row;
                        }

                        if (safe_end > safe_start)
                        {
                            _lib.ComputeGeneration(currentBuffer, nextBuffer, rowsize, safe_start, safe_end, total_rows);
                        }
                    });

                    // Swap Buffers
                    byte[] temp = currentBuffer;
                    currentBuffer = nextBuffer;
                    nextBuffer = temp;
                }

                sw.Stop();
                AppendTextThreadSafe($"JACpp thread pool completed {generations} generations in {sw.ElapsedMilliseconds} ms", false);

                System.Runtime.InteropServices.Marshal.Copy(currentBuffer, 0, ptr, bytes);
            }
            else if (radioasmchecked)
            {
                Stopwatch sw2 = Stopwatch.StartNew();

                for (int g = 0; g < generations; g++)
                {
                    // Use ThreadPool via Parallel.For for maximum speed
                    Parallel.For(0, thread_count, i =>
                    {
                        int start_row = i * rows_per_thread;
                        int end_row = 1;

                        int safe_start = 1;
                        int safe_end = 1;

                        if (i == thread_count - 1)
                        {
                            end_row = total_rows;
                        }
                        else
                        {
                            end_row = start_row + rows_per_thread;
                        }

                        if (start_row == 0)
                        {
                            safe_start = 1;
                        }
                        else
                        {
                            safe_start = start_row;
                        }

                        if (end_row == total_rows)
                        {
                            safe_end = total_rows - 1;
                        }
                        else
                        {
                            safe_end = end_row;
                        }



                        if (safe_end > safe_start)
                        {
                            MyProc1(currentBuffer, nextBuffer, rowsize, safe_start, safe_end, total_rows);
                        }
                    });

                    // Swap Buffers
                    byte[] temp = currentBuffer;
                    currentBuffer = nextBuffer;
                    nextBuffer = temp;
                }

                sw2.Stop();
                AppendTextThreadSafe($"Asm thread pool completed {generations} generations in {sw2.ElapsedMilliseconds} ms", false);

                System.Runtime.InteropServices.Marshal.Copy(currentBuffer, 0, ptr, bytes);
            }
            else if (radiocmpchecked)
            {
                // TODO: Implement comparison
            }

            first_gen.UnlockBits(bmpData);
            Mybmp.Source = BitmapHelper.ToWpfBitmap(first_gen);
            try
            {
                first_gen.Save("modified.bmp");
            }
            catch { }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e) { }
        private void textbox_threads_TextChanged(object sender, TextChangedEventArgs e) { }

        
        private void MyProc1Thread(object state)
        {
            var args = (MyProc1Args)state;
            int threadId = Thread.CurrentThread.ManagedThreadId;
            var sw = Stopwatch.StartNew();

            MyProc1(args.Src, args.Dst, args.RowSize, args.StartRow, args.EndRow, args.TotalHeight);

            sw.Stop();
            AppendTextThreadSafe($"Asm Thread {threadId}: computed in {sw.ElapsedMilliseconds} ms", true);
        }

        private void CppProcThread(object state)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            var sw = Stopwatch.StartNew();
            var args = (CppProcArgs)state;

            args.Lib.ComputeGeneration(args.Src, args.Dst, args.RowSize, args.StartRow, args.EndRow, args.TotalHeight);

            sw.Stop();
            AppendTextThreadSafe($"C++ Thread {threadId}: computed in {sw.ElapsedMilliseconds} ms", true);
        }

        private class CppProcArgs
        {
            public byte[] Src { get; set; }
            public byte[] Dst { get; set; }
            public int RowSize { get; set; }
            public int StartRow { get; set; }
            public int EndRow { get; set; }
            public int TotalHeight { get; set; }
            public NativeAddLibrary Lib { get; set; }
        }

        private class MyProc1Args
        {
            public byte[] Src { get; set; }
            public byte[] Dst { get; set; }
            public int RowSize { get; set; }
            public int StartRow { get; set; }
            public int EndRow { get; set; }
            public int TotalHeight { get; set; }
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            initTestDll();
            var sw = Stopwatch.StartNew();
            Thread.Sleep(600);
            sw.Stop();
            AppendTextThreadSafe($"JACpp procedure: computed in {sw.ElapsedMilliseconds} ms", false);
        }

        private void AppendTextThreadSafe(string text, bool thread)
        {
            if (textboxresults.Dispatcher.CheckAccess())
            {
                if (thread)
                    textboxthreadresults.Text += text + "\n";
                else
                    textboxresults.Text += text + "\n";
            }
            else
            {
                textboxresults.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (thread)
                        textboxthreadresults.Text += text + "\n";
                    else
                        textboxresults.Text += text + "\n";
                }));
            }
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            this.radiocppchecked = true;
            this.radioasmchecked = false;
            this.radiocmpchecked = false;
        }

        private void RadioButton_Checked_1(object sender, RoutedEventArgs e)
        {
            this.radioasmchecked = true;
            this.radiocppchecked = false;
            this.radiocmpchecked = false;
        }

        private void RadioButton_Checked_2(object sender, RoutedEventArgs e)
        {
            this.radiocmpchecked = true;
            this.radiocppchecked = false;
            this.radiocmpchecked = false;
        }

        private void TextBox_TextChanged_1(object sender, TextChangedEventArgs e) { }
    }
}