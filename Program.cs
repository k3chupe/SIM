// Tę linię dodajemy, aby wyłączyć ostrzeżenia o nullach w nowym .NET
#nullable disable

using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace SimCardReaderGui
{
    public class MainForm : Form
    {
        // Import biblioteki winscard
        [DllImport("winscard.dll")]
        static extern int SCardEstablishContext(uint dwScope, IntPtr pvReserved1, IntPtr pvReserved2, out IntPtr phContext);

        [DllImport("winscard.dll", EntryPoint = "SCardListReadersA", CharSet = CharSet.Ansi)]
        static extern int SCardListReaders(IntPtr hContext, byte[] mszGroups, byte[] mszReaders, ref uint pcchReaders);

        [DllImport("winscard.dll", EntryPoint = "SCardConnectA", CharSet = CharSet.Ansi)]
        static extern int SCardConnect(IntPtr hContext, string szReader, uint dwShareMode, uint dwPreferredProtocols, out IntPtr phCard, out uint pdwActiveProtocol);

        [DllImport("winscard.dll")]
        static extern int SCardTransmit(IntPtr hCard, ref SCARD_IO_REQUEST pioSendPci, byte[] pbSendBuffer, int cbSendLength, IntPtr pioRecvPci, byte[] pbRecvBuffer, ref int pcbRecvLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct SCARD_IO_REQUEST
        {
            public uint dwProtocol;
            public int cbPciLength;
        }

        // Elementy interfejsu
        private ComboBox comboReaders;
        private Button btnConnect;
        private Button btnReadSms;
        private Button btnReadContacts;
        private Button btnSendApdu;
        private TextBox txtApduInput;
        private RichTextBox rtbLog;

        // Zmienne do polaczenia
        private IntPtr hContext;
        private IntPtr hCard;
        private uint activeProtocol;

        public MainForm()
        {
            this.Text = "Czytnik Kart SIM";
            this.Size = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterScreen;

            InitControls();
            LoadReaders();
        }

        private void InitControls()
        {
            Label lblReader = new Label() { Text = "Wybierz czytnik", Location = new Point(10, 10), AutoSize = true };
            this.Controls.Add(lblReader);

            comboReaders = new ComboBox() { Location = new Point(10, 30), Width = 250 };
            this.Controls.Add(comboReaders);

            btnConnect = new Button() { Text = "Polacz", Location = new Point(270, 29), Width = 100 };
            btnConnect.Click += BtnConnect_Click;
            this.Controls.Add(btnConnect);

            Label lblApdu = new Label() { Text = "Wlasna komenda APDU HEX", Location = new Point(10, 70), AutoSize = true };
            this.Controls.Add(lblApdu);

            txtApduInput = new TextBox() { Location = new Point(10, 90), Width = 250, Text = "A0A40000023F00" };
            this.Controls.Add(txtApduInput);

            btnSendApdu = new Button() { Text = "Wyslij APDU", Location = new Point(270, 89), Width = 100 };
            btnSendApdu.Click += BtnSendApdu_Click;
            this.Controls.Add(btnSendApdu);

            btnReadContacts = new Button() { Text = "Czytaj Kontakty", Location = new Point(10, 130), Width = 180 };
            btnReadContacts.Click += (s, e) => ReadSimFile("Kontakty");
            this.Controls.Add(btnReadContacts);

            btnReadSms = new Button() { Text = "Czytaj SMS", Location = new Point(200, 130), Width = 180 };
            btnReadSms.Click += (s, e) => ReadSimFile("SMS");
            this.Controls.Add(btnReadSms);

            rtbLog = new RichTextBox() { Location = new Point(10, 170), Width = 560, Height = 280, ReadOnly = true, BackColor = Color.White };
            this.Controls.Add(rtbLog);
        }

        private void LoadReaders()
        {
            int ret = SCardEstablishContext(2, IntPtr.Zero, IntPtr.Zero, out hContext);
            if (ret != 0)
            {
                Log("Blad uslugi SmartCard");
                return;
            }

            uint pcchReaders = 0;
            // Przekazujemy null, null - dzieki #nullable disable kompilator to zaakceptuje
            SCardListReaders(hContext, null, null, ref pcchReaders);
            
            byte[] mszReaders = new byte[pcchReaders];
            SCardListReaders(hContext, null, mszReaders, ref pcchReaders);

            string[] readers = Encoding.ASCII.GetString(mszReaders).Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var r in readers)
                comboReaders.Items.Add(r);

            if (comboReaders.Items.Count > 0)
                comboReaders.SelectedIndex = 0;
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (comboReaders.SelectedItem == null) return;
            string readerName = comboReaders.SelectedItem.ToString();

            int ret = SCardConnect(hContext, readerName, 2, 3, out hCard, out activeProtocol);
            if (ret == 0)
                Log("Polaczono z " + readerName);
            else
                Log("Blad polaczenia Kod: " + ret);
        }

        private void BtnSendApdu_Click(object sender, EventArgs e)
        {
            try
            {
                byte[] apdu = StringToByteArray(txtApduInput.Text);
                byte[] response = SendAPDU(apdu);
                Log("Wyslano: " + txtApduInput.Text);
                Log("Otrzymano: " + BitConverter.ToString(response));
            }
            catch
            {
                Log("Blad formatu HEX");
            }
        }

        private void ReadSimFile(string fileType)
        {
            Log("Rozpoczynam odczyt: " + fileType);
            
            SendAPDU(new byte[] { 0xA0, 0xA4, 0x00, 0x00, 0x02, 0x3F, 0x00 });
            SendAPDU(new byte[] { 0xA0, 0xA4, 0x00, 0x00, 0x02, 0x7F, 0x10 });

            byte[] fileId;
            int len;

            if (fileType == "Kontakty")
            {
                fileId = new byte[] { 0x6F, 0x3A };
                len = 32;
            }
            else
            {
                fileId = new byte[] { 0x6F, 0x3C };
                len = 176;
            }

            SendAPDU(new byte[] { 0xA0, 0xA4, 0x00, 0x00, 0x02, fileId[0], fileId[1] });
            SendAPDU(new byte[] { 0xA0, 0xC0, 0x00, 0x00, 0x0F });

            for (int i = 1; i <= 5; i++)
            {
                byte[] cmd = new byte[] { 0xA0, 0xB2, (byte)i, 0x04, (byte)len };
                byte[] data = SendAPDU(cmd);

                if (data.Length > 2 && data[0] != 0xFF && data[0] != 0x00)
                {
                    string ascii = "";
                    foreach (byte b in data)
                    {
                        if (Char.IsLetterOrDigit((char)b) || Char.IsPunctuation((char)b) || b == ' ')
                            ascii += (char)b;
                        else
                            ascii += ".";
                    }
                    Log("Rekord " + i + " HEX: " + BitConverter.ToString(data));
                    Log("ASCII: " + ascii);
                    Log("----------------");
                }
            }
            Log("Zakonczono odczyt.");
        }

        private byte[] SendAPDU(byte[] cmd)
        {
            SCARD_IO_REQUEST pioSendPci = new SCARD_IO_REQUEST();
            pioSendPci.dwProtocol = activeProtocol;
            pioSendPci.cbPciLength = 8;

            byte[] recv = new byte[256];
            int recvLen = recv.Length;

            int ret = SCardTransmit(hCard, ref pioSendPci, cmd, cmd.Length, IntPtr.Zero, recv, ref recvLen);
            
            if (ret != 0) return new byte[0];

            byte[] result = new byte[recvLen];
            Array.Copy(recv, result, recvLen);
            return result;
        }

        private void Log(string msg)
        {
            rtbLog.AppendText(msg + "\n");
            rtbLog.ScrollToCaret();
        }

        private byte[] StringToByteArray(string hex)
        {
            hex = hex.Replace(" ", "");
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}