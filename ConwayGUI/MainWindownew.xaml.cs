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

namespace ConwayGUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 

    public class NativeAddLibrary : IDisposable
    {
        // -----------------------------
        // Kernel32 imports
        // -----------------------------
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);


        // -----------------------------
        // Delegate matching the C++ function
        // -----------------------------
       
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ComputeGenerationDelegate(
            byte[] src,
            byte[] dst,
            int rowsize,
            int start_row,
            int end_row,
            int total_height
        );

        // ↑↑ CHANGED: Added byte[] and int length parameters
        // ↑↑ CHANGED: Corrected name capitalization pattern

        
        private ComputeGenerationDelegate _computeGeneration;

        private IntPtr _dllHandle = IntPtr.Zero;



        public bool Load(string dllPath)
        {
            _dllHandle = LoadLibrary(dllPath);
            if (_dllHandle == IntPtr.Zero)
                return false;

            IntPtr compPtr = GetProcAddress(_dllHandle, "computeGeneration");
            if (compPtr == IntPtr.Zero)
                return false;

            _computeGeneration = Marshal.GetDelegateForFunctionPointer<ComputeGenerationDelegate>(compPtr);
            return true;
        }

        // Updated to match new signature
        public int ComputeGeneration(byte[] src, byte[] dst, int rowsize, int start_row, int end_row, int total_height)
        {
            if (_computeGeneration == null)
                throw new InvalidOperationException("computeGeneration pointer is NULL!");

            return _computeGeneration(src, dst, rowsize, start_row, end_row, total_height);
        }


        // -----------------------------
        // Unload
        // -----------------------------
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
                // According to MSDN, "The default OnDemand cache option retains access to the stream until the image is needed."
                // Force the bitmap to load right now so we can dispose the stream.
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

        [DllImport(@"JAAsm.dll")]
        unsafe static extern int MyProc1([In, Out] byte[] bytes, [In, Out] byte[] results, int offset , int rowsize, int count);


        private readonly NativeAddLibrary _lib = new NativeAddLibrary();

        public void initTestDll()
        {
            if (!_lib.Load("JACpp.dll"))   // DLL must be beside .exe
                throw new Exception("Could not load JACpp.dll");
        }

      
        public MainWindow()
        {

            InitializeComponent();
            var cpu_count = Environment.ProcessorCount;
            //textbox_threads.Text = cpuCount.ToString();
            label_threads.Content = cpu_count.ToString();
            textbox_threads.Text = cpu_count.ToString();
        }


     

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                first_gen = new Bitmap("first_generation.bmp", false);
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
            }

            // Lock the bitmap's bits.  
            System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, first_gen.Width, first_gen.Height);
            System.Drawing.Imaging.BitmapData bmpData =
                first_gen.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                first_gen.PixelFormat);

            // Get the address of the first line.
            IntPtr ptr = bmpData.Scan0;

            // Declare an array to hold the bytes of the bitmap.
            int bytes = Math.Abs(bmpData.Stride) * first_gen.Height;
            byte[] rgbValues = new byte[bytes];

            // get bitmap dimensions
            bitmap_length = rgbValues.Length;
            bitmap_height = first_gen.Height;

            //get thread count
            try 
            {
                thread_count = int.Parse(textbox_threads.Text);
            }
            catch 
            { 
                thread_count = 1; 
            }
           
            // Copy the RGB values into the array.
            System.Runtime.InteropServices.Marshal.Copy(ptr, rgbValues, 0, bytes);


            if (thread_count == 0)
            {
                thread_count = 1;
            }

            if (radiocppchecked)
            {
                //call computeGeneration from C++ DLL
                initTestDll();
                byte[] cppresult = new byte[bytes];

                int rowsize = bmpData.Stride;
                
                // Calculate rows per thread
                int total_rows = first_gen.Height;
                int rows_per_thread = total_rows / thread_count;

                Stopwatch sw = Stopwatch.StartNew();

                var threads = new List<Thread>();

                for (int i = 0; i < thread_count; i++)
                {
                    int start_row = i * rows_per_thread;
                    int end_row;

                    // Ensure last thread processes remaining rows
                    if (i == thread_count - 1)
                    {
                        end_row = total_rows;
                    }
                    else
                    {
                        end_row = start_row + rows_per_thread;
                    }

                    var cppArgs = new CppProcArgs
                    {
                        Src = rgbValues,
                        Dst = cppresult,
                        RowSize = rowsize,
                        StartRow = start_row,
                        EndRow = end_row,
                        TotalHeight = total_rows,
                        Lib = _lib
                    };

                    Thread t = new Thread(new ParameterizedThreadStart(CppProcThread));
                    threads.Add(t);
                    t.Start(cppArgs);
                }

                foreach (var t in threads)
                    t.Join();

                sw.Stop();
                AppendTextThreadSafe($"JACpp completed in {sw.ElapsedMilliseconds} ms");

                // Copy result back to original array for display
                Array.Copy(cppresult, rgbValues, bytes);
            }
            else if (radioasmchecked)
            {
                byte[] asmresult = new byte[bytes];

                int row_stride = bmpData.Stride;

                int workable_length = bitmap_length - (2 * row_stride);

                int base_chunk_size = workable_length / thread_count;

                var threads = new List<Thread>();
                for (int i = 0; i < thread_count; i++)
                {
                    
                    int current_offset = row_stride + (i * base_chunk_size);

                    int current_count;
                    if (i == thread_count - 1)
                    {
                        
                        current_count = (bitmap_length - row_stride) - current_offset;
                    }
                    else
                    {
                        current_count = base_chunk_size;
                    }

                    var myArgs = new MyProc1Args
                    {
                        Bytes = rgbValues,
                        Results = asmresult,
                        Offset = current_offset,
                        RowSize = bmpData.Stride,
                        Count = current_count
                    };

                    Thread t = new Thread(new ParameterizedThreadStart(MyProc1Thread));
                    threads.Add(t);
                    t.Start(myArgs);
                }

                
                foreach (var t in threads)
                    t.Join();

                Array.Copy(asmresult, rgbValues, bytes);
                
            }
            else if (radiocmpchecked){
                //TODO implement comparing both methods

            }


            System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, ptr, bytes);
            first_gen.UnlockBits(bmpData);
            Mybmp.Source = BitmapHelper.ToWpfBitmap(first_gen);
            try
            {
                first_gen.Save("modified.bmp");
            }
            catch {}
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void textbox_threads_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        // Add a wrapper method that matches ParameterizedThreadStart signature
        private void MyProc1Thread(object state)
        {
            // Unpack arguments from state object
            var args = (MyProc1Args)state;
            //Console.WriteLine("proces z offsetem :" + args.Offset.ToString());

            //call procedure once to load into cache
            MyProc1(args.Bytes, args.Results, args.Offset, args.RowSize, args.Count);

            // I kept the timing logic here as it was in your file
            // though it might be redundant with the main Button_Click timer now
            /*
            int threadId = Thread.CurrentThread.ManagedThreadId;
            var sw = Stopwatch.StartNew();
            MyProc1(args.Bytes, args.Results, args.Offset, args.RowSize, args.Count);
            sw.Stop();

            AppendTextThreadSafe($"JAAsm thread {threadId} completed in {sw.ElapsedMilliseconds} ms");
            */
        }

        // Wrapper for C++ Thread
        private void CppProcThread(object state)
        {
            var args = (CppProcArgs)state;
            args.Lib.ComputeGeneration(args.Src, args.Dst, args.RowSize, args.StartRow, args.EndRow, args.TotalHeight);
        }

        // Arguments Class for C++
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

        // Define a class to hold arguments for the thread
        private class MyProc1Args
        {
            public byte[] Bytes { get; set; }
            public byte[] Results { get; set; }
            public int Offset { get; set; }
            public int RowSize { get; set; }
            public int Count { get; set; }
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {

            initTestDll();

           

            var sw = Stopwatch.StartNew();
            

            Thread.Sleep(600);
            sw.Stop();

            textboxresults.Text += $"JACpp procedure:  (computed in {sw.ElapsedMilliseconds} ms)\n";

        }


        private void AppendTextThreadSafe(string text)
        {
            // Always use BeginInvoke to avoid blocking threads
            if (textboxresults.Dispatcher.CheckAccess())
            {
                textboxresults.Text += text + "\n";
            }
            else
            {
                textboxresults.Dispatcher.BeginInvoke(new Action(() =>
                {
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

        private void TextBox_TextChanged_1(object sender, TextChangedEventArgs e)
        {

        }
    }
}