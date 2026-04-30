using AForge.Video;
using AForge.Video.DirectShow;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TcpVideoClient;

public sealed class MainForm : Form
{
    private readonly Button connectCameraButton;
    private readonly Button connectServerButton;
    private readonly TextBox ipTextBox;
    private readonly Label statusLabel;
    private readonly PictureBox previewBox;

    private FilterInfoCollection? videoDevices;
    private VideoCaptureDevice? videoSource;

    private TcpClient? tcpClient;
    private NetworkStream? networkStream;
    private readonly object streamLock = new();

    private bool frameSaved;

    public MainForm()
    {
        Text = "TCP视频客户端";
        Width = 900;
        Height = 620;

        connectCameraButton = new Button { Text = "连接摄像头", Left = 20, Top = 20, Width = 160, Height = 40 };
        connectServerButton = new Button { Text = "连接服务器并发送", Left = 200, Top = 20, Width = 200, Height = 40 };
        ipTextBox = new TextBox { Left = 420, Top = 28, Width = 180, Text = "127.0.0.1" };
        statusLabel = new Label { Left = 620, Top = 32, Width = 240, Text = "未连接" };
        previewBox = new PictureBox
        {
            Left = 20,
            Top = 80,
            Width = 840,
            Height = 480,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(connectCameraButton);
        Controls.Add(connectServerButton);
        Controls.Add(ipTextBox);
        Controls.Add(statusLabel);
        Controls.Add(previewBox);

        connectCameraButton.Click += ConnectCameraButton_Click;
        connectServerButton.Click += ConnectServerButton_Click;
        FormClosing += MainForm_FormClosing;
    }

    private void ConnectCameraButton_Click(object? sender, EventArgs e)
    {
        videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
        if (videoDevices.Count == 0)
        {
            MessageBox.Show("未检测到摄像头设备");
            return;
        }

        videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
        videoSource.NewFrame += OnNewFrame;
        videoSource.Start();
        statusLabel.Text = $"摄像头已连接：{videoDevices[0].Name}";
    }

    private async void ConnectServerButton_Click(object? sender, EventArgs e)
    {
        try
        {
            tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(ipTextBox.Text.Trim(), 8888);
            networkStream = tcpClient.GetStream();
            statusLabel.Text = "已连接服务器，正在发送...";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"连接服务器失败: {ex.Message}");
        }
    }

    private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
    {
        using var frame = (Bitmap)eventArgs.Frame.Clone();

        if (!frameSaved)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"single_frame_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
            frame.Save(path, ImageFormat.Jpeg);
            frameSaved = true;
        }

        Invoke(new Action(() =>
        {
            previewBox.Image?.Dispose();
            previewBox.Image = (Bitmap)frame.Clone();
        }));

        lock (streamLock)
        {
            if (networkStream == null || !networkStream.CanWrite)
            {
                return;
            }

            try
            {
                using var ms = new MemoryStream();
                frame.Save(ms, ImageFormat.Jpeg);
                byte[] imgBytes = ms.ToArray();
                byte[] lenBytes = BitConverter.GetBytes(imgBytes.Length);

                networkStream.Write(lenBytes, 0, lenBytes.Length);
                networkStream.Write(imgBytes, 0, imgBytes.Length);
                networkStream.Flush();
            }
            catch
            {
                // 忽略瞬时网络错误，保持采集线程持续运行。
            }
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (videoSource is { IsRunning: true })
        {
            videoSource.SignalToStop();
            videoSource.WaitForStop();
        }

        networkStream?.Close();
        tcpClient?.Close();
    }
}
