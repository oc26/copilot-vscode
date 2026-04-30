using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TcpVideoReceiver;

public sealed class MainForm : Form
{
    private readonly Button startListenButton;
    private readonly Label statusLabel;
    private readonly PictureBox videoBox;

    private TcpListener? listener;
    private CancellationTokenSource? cts;

    public MainForm()
    {
        Text = "TCP视频接收端";
        Width = 900;
        Height = 620;

        startListenButton = new Button { Text = "启动监听", Left = 20, Top = 20, Width = 160, Height = 40 };
        statusLabel = new Label { Text = "未监听", Left = 200, Top = 30, Width = 500 };
        videoBox = new PictureBox
        {
            Left = 20,
            Top = 80,
            Width = 840,
            Height = 480,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(startListenButton);
        Controls.Add(statusLabel);
        Controls.Add(videoBox);

        startListenButton.Click += StartListenButton_Click;
        FormClosing += MainForm_FormClosing;
    }

    private async void StartListenButton_Click(object? sender, EventArgs e)
    {
        startListenButton.Enabled = false;
        cts = new CancellationTokenSource();

        listener = new TcpListener(IPAddress.Any, 8888);
        listener.Start();
        statusLabel.Text = "监听中: 0.0.0.0:8888";

        try
        {
            while (!cts.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cts.Token);
                statusLabel.Text = "客户端已连接，接收视频中...";
                _ = Task.Run(() => ReceiveLoop(client, cts.Token), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "监听已停止";
        }
    }

    private void ReceiveLoop(TcpClient client, CancellationToken token)
    {
        using (client)
        using NetworkStream stream = client.GetStream();

        while (!token.IsCancellationRequested)
        {
            byte[] lenBytes = ReadExact(stream, 4, token);
            int imageLength = BitConverter.ToInt32(lenBytes, 0);

            byte[] imageBytes = ReadExact(stream, imageLength, token);
            using var ms = new MemoryStream(imageBytes);
            using Image frame = Image.FromStream(ms);
            Bitmap cloned = new(frame);

            Invoke(new Action(() =>
            {
                videoBox.Image?.Dispose();
                videoBox.Image = cloned;
            }));
        }
    }

    private static byte[] ReadExact(NetworkStream stream, int size, CancellationToken token)
    {
        byte[] buffer = new byte[size];
        int offset = 0;

        while (offset < size)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, offset, size - offset);
            if (read == 0)
            {
                throw new IOException("连接已断开");
            }

            offset += read;
        }

        return buffer;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        cts?.Cancel();
        listener?.Stop();
    }
}
