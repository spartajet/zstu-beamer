using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImageWriter.Annotations;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Path = System.IO.Path;

#nullable enable
namespace ImageWriter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window, INotifyPropertyChanged
    {
        /// <summary>
        /// 串口对象
        /// </summary>
        private SerialPort serialPort;

        /// <summary>
        /// 串口是否连接Flag
        /// </summary>
        private bool serialPortConnectedFlag = false;

        /// <summary>
        /// 图像加载Flag
        /// </summary>
        private bool imageLoadFlag = false;

        // private bool frameSendFlag = true;

        /// <summary>
        /// 串口索引
        /// </summary>
        private int serialPortNameSelectedIndex = 0;

        /// <summary>
        /// 选择的波特率
        /// </summary>
        private int baudRate = 115200;

        /// <summary>
        /// 选择的校验位
        /// </summary>
        private string parityBit = "None";

        /// <summary>
        /// 选择的数据位
        /// </summary>
        private int dataBit = 8;

        /// <summary>
        /// 选择的停止位
        /// </summary>
        private double stopBit = 1;

        /// <summary>
        /// 图像文件名
        /// </summary>
        private string imageFileName = "请选择.jpg";

        /// <summary>
        /// 发送的进度条数值
        /// </summary>
        private int sendProgressCount = 0;

        /// <summary>
        /// 发送帧数值
        /// </summary>
        private int sendCount = 0;

        /// <summary>
        /// 接收的进度条数值
        /// </summary>
        private int receiveProgressCount = 0;

        /// <summary>
        /// 接收的成功数值
        /// </summary>
        private int receiveCount = 0;

        /// <summary>
        /// 原始图像Mat
        /// </summary>
        private Mat originMat;

        /// <summary>
        /// 16位Mat
        /// </summary>
        private Mat grayHexMat;

        /// <summary>
        /// 发送的临时数据
        /// </summary>
        private byte[] tempSendBytes;

        /// <summary>
        /// 发送Task
        /// </summary>
        private Task sendTask;

        /// <summary>
        /// 16位图像的内存地址
        /// </summary>
        private IntPtr imageStartPtr;

        /// <summary>
        /// 串口控制按钮事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SerialControlButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!this.serialPortConnectedFlag)
            {
                this.serialPort = new SerialPort()
                {
                    PortName = this.SerialPortNameItems[this.SerialPortNameSelectedIndex],
                    BaudRate = this.BaudRate,
                    DataBits = this.DataBit,
                    Parity = (Parity) Enum.Parse(typeof(Parity), this.ParityBit),
                    StopBits = (StopBits) Enum.Parse(typeof(StopBits), this.StopBit.ToString()),
                    ReadBufferSize = 8192,
                    WriteBufferSize = 102400,
                    ReadTimeout = 2000,
                    WriteTimeout = 2000
                };
                try
                {
                    this.serialPort.Open();
                    this.serialPort.DataReceived += this.SerialPort_DataReceived;
                    this.SerialStatusLed.Fill = new SolidColorBrush(Colors.LimeGreen);
                    this.serialPortConnectedFlag = true;
                    this.SerialControlButton.Content = "断开";
                    this.StatusBlock.Text = "串口已打开";
                    this.ImageOperateGroup.IsEnabled = true;
                    this.TransOperateGroup.IsEnabled = true;
                }
                catch (Exception exception)
                {
                    MessageBox.Show(exception.ToString(), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                this.serialPort.Close();
                this.serialPortConnectedFlag = false;
                this.SerialStatusLed.Fill = new SolidColorBrush(Colors.Red);
                this.SerialControlButton.Content = "连接";
                this.StatusBlock.Text = "串口已关闭";
                this.ImageOperateGroup.IsEnabled = false;
                this.TransOperateGroup.IsEnabled = false;
            }
        }

        /// <summary>
        /// 打开图像
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenImageButton_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog()
            {
                Title = "打开图像文件",
                Filter = "JPG图像|*.jpg|PNG图像|*.png",
                Multiselect = false
            };
            bool? dialogResult = openFileDialog.ShowDialog();
            if (dialogResult != true)
            {
                return;
            }

            string fileName = openFileDialog.FileName;
            this.ImageFileName = Path.GetFileName(fileName);
            this.originMat = Cv2.ImRead(fileName);
            if (this.originMat.Type() != MatType.CV_8UC3 || this.originMat is not { Cols: 640 } || this.originMat is not
                { Rows: 512 })
            {
                MessageBox.Show(this, "图像格式不正确，请选择8UC3且640*512格式图像", "警告", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Bitmap originBitmap = this.originMat.ToBitmap();
            this.MainImageControl.Source = this.BitmapToImageSource(originBitmap);
            Mat[] channelMats = Cv2.Split(this.originMat);
            // Mat gary8Mat = this.originMat.CvtColor(ColorConversionCodes.RGB2GRAY);
            this.grayHexMat = new Mat();
            channelMats[1].ConvertTo(this.grayHexMat, MatType.CV_16UC1, 1);
            Cv2.ImWrite("a.png", this.grayHexMat, new ImageEncodingParam(ImwriteFlags.PngCompression, 0));
            this.SendButton.IsEnabled = true;
            this.StopSendButton.IsEnabled = true;
            this.StatusBlock.Text = "打开图像成功";
        }

        /// <summary>
        /// 发送按钮事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.tempSendBytes = new byte[16388];
            this.tempSendBytes[0] = 0xAA;
            this.tempSendBytes[1] = 0xBB;
            this.tempSendBytes[16386] = 0xCC;
            this.tempSendBytes[16387] = 0xDD;
            //清空串口缓存
            this.serialPort.DiscardInBuffer();
            this.serialPort.DiscardOutBuffer();
            this.imageStartPtr = this.grayHexMat.DataStart;
            this.SendFrame(0);
        }

        /// <summary>
        /// 发送某一帧数据
        /// </summary>
        /// <param name="index">帧序号，从0开始</param>
        private void SendFrame(int index)
        {
            IntPtr ptr = new IntPtr(this.imageStartPtr.ToInt64() + 16384 * index);
            Marshal.Copy(ptr, this.tempSendBytes, 2, 16384);

            this.serialPort.Write(this.tempSendBytes, 0, 4096);
            Task.Delay(TimeSpan.FromMilliseconds(200));
            this.serialPort.Write(this.tempSendBytes, 4096, 4096);
            Task.Delay(TimeSpan.FromMilliseconds(200));
            this.serialPort.Write(this.tempSendBytes, 8192, 4096);
            Task.Delay(TimeSpan.FromMilliseconds(200));
            this.serialPort.Write(this.tempSendBytes, 12288, 4096);
            Task.Delay(TimeSpan.FromMilliseconds(200));
            this.serialPort.Write(this.tempSendBytes, 16384, 4);
            int temp = index + 1;
            Application.Current.Dispatcher.Invoke(() =>
            {
                this.SendCount = temp;
                this.SendProgressCount = (int) (this.SendCount * 2.5);
                this.StatusBlock.Text = $"发送第{this.sendCount}帧数据,待下位机确认是否收到该帧数据";
            });
        }

        /// <summary>
        /// 串口数据接收
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            while (this.serialPort.BytesToRead < 7)
            {
                Task.Delay(TimeSpan.FromMilliseconds(200));
            }

            byte[] receive = new byte[this.serialPort.BytesToRead];
            this.serialPort.Read(receive, 0, this.serialPort.BytesToRead);
            byte messageType = receive[2];
            byte messageCount = receive[3];
            // 某一帧的结果
            if (messageType == 0x01)
            {
                if (messageCount < 40)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        this.StatusBlock.Text = $"下位机已成功接收第{messageCount}帧,准备发送下一帧";

                    });
                    this.SendFrame(messageCount);
                }
                else if (messageCount == 40)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        this.StatusBlock.Text = $"下位机已成功接收第{messageCount}帧,发送完毕，等待确认";
                    });
                }
            }
            else if (messageType == 0x02)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    this.StatusBlock.Text = $"下位机确认整幅图像发送成功！";
                });
            }

            // this.frameSendFlag = true;
        }

        public MainWindow()
        {
            this.InitializeComponent();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 可用串口名称列表
        /// </summary>
        public string[] SerialPortNameItems { get; set; } = SerialPort.GetPortNames();


        /// <summary>
        /// 波特率列表
        /// </summary>
        public int[] BaudRateItems { get; set; } =
        {
            300, 600, 1200, 2400, 4800, 9600, 19200, 38400, 56000, 57600, 115200, 128000, 230400, 256000, 460800,
            512000, 921600
        };


        /// <summary>
        /// 校验位列表
        /// </summary>
        public string[] ParityBitItems { get; set; } = {"None", "Odd", "Even", "Mark(=1)", "Space(=0)"};


        /// <summary>
        /// 数据位列表
        /// </summary>
        public int[] DataBitItems { get; set; } = {5, 6, 7, 8};

        /// <summary>
        /// 停止位列表
        /// </summary>
        public double[] StopBitItems { get; set; } = {1, 1.5, 2};

        /// <summary>
        /// 选择的校验位
        /// </summary>
        public string ParityBit
        {
            get => this.parityBit;
            set
            {
                this.parityBit = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// 选择的数据位
        /// </summary>
        public int DataBit
        {
            get => this.dataBit;
            set
            {
                this.dataBit = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// 选择的停止位
        /// </summary>
        public double StopBit
        {
            get => this.stopBit;
            set
            {
                this.stopBit = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// 串口索引
        /// </summary>
        public int SerialPortNameSelectedIndex
        {
            get => this.serialPortNameSelectedIndex;
            set
            {
                this.serialPortNameSelectedIndex = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// 选择的波特率
        /// </summary>
        public int BaudRate
        {
            get => this.baudRate;
            set
            {
                this.baudRate = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// 图像文件名
        /// </summary>
        public string ImageFileName
        {
            get => this.imageFileName;
            set
            {
                this.imageFileName = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// 发送的进度条数值
        /// </summary>
        public int SendProgressCount
        {
            get => this.sendProgressCount;
            set
            {
                this.sendProgressCount = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// 发送帧数值
        /// </summary>
        public int SendCount
        {
            get => this.sendCount;
            set
            {
                this.sendCount = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// 接收的进度条数值
        /// </summary>
        public int ReceiveProgressCount
        {
            get => this.receiveProgressCount;
            set
            {
                this.receiveProgressCount = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// 接收的成功数值
        /// </summary>
        public int ReceiveCount
        {
            get => this.receiveCount;
            set
            {
                this.receiveCount = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// Image 显示Bitmap
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using MemoryStream memory = new MemoryStream();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
            memory.Position = 0;
            BitmapImage bitmapimage = new BitmapImage();
            bitmapimage.BeginInit();
            bitmapimage.StreamSource = memory;
            bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapimage.EndInit();
            return bitmapimage;
        }
    }
}