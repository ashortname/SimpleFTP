using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FTP_Winform
{
    public partial class Form1 : Form
    {       
        private String user; /*账号*/
        private String pass; /*密码*/
        private ImageList imagelist; /*图标集*/
        private List<DataModel> currentDirList = new List<DataModel>(); /*当前目录下的文件以及文件夹*/
        private List<DataModel> currentFileList = new List<DataModel>();
        private static String RootUrl = ""; /*默认的根目录*/
        private static String LocalSavePath = "./FtpDownLoad"; /*默认的保存路径*/
        private String currentUrl = ""; /*当前路径*/
        private bool USESSL = false; /*是否启用ssl*/
        private int LastSelectedIndex = -1; /*上次选择的用户名-账号组合*/
        private String LastLoginIP = "";

        private Thread downloads; /*下载线程*/
        private Thread uploads; /*上传线程*/

        private delegate void ChangeProgress(int va); /*progressBar的委托*/
        private ChangeProgress changep1, changep2;

        private delegate String GetListView(); /*listview的委托*/
        private GetListView getName, getType;

        private List<A_PS> FtpData; /*账号-密码对模型*/

        private double tick1 = 0;
        private double tick2 = 0;
        
        public Form1()
        {
            InitializeComponent();
        }
        /// <summary>
        /// 窗体加载
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_Load(object sender, EventArgs e)
        {
            //调用初始化
            Initil();
        }

        #region---初始化操作---
        /// <summary>
        /// 初始化
        /// </summary>
        private void Initil()
        {
            //添加图标
            imagelist = new ImageList();
            imagelist.ImageSize = new Size(32,32);
            imagelist.Images.Add(Properties.Resources.direct);
            imagelist.Images.Add(Properties.Resources.document);
            imagelist.ColorDepth = ColorDepth.Depth32Bit;
            listView1.LargeImageList = imagelist;

            //加密方式选择
            comboEncode.SelectedIndex = 1;

            //初始化线程
            downloads = new Thread(DownloadFile);
            uploads = new Thread(UploadFile);

            //初始化委托
            changep1 = new ChangeProgress(ChangeP1);
            changep2 = new ChangeProgress(ChangeP2);

            //listview操作
            getName = new GetListView(GetListName);
            getType = new GetListView(GetListType);

            //
            btnDisCon.Enabled = false;

            //账号密码操作
            FtpData = new List<A_PS>();           
            String temp = XmlHelper.getLastChoice();
            if(!temp.Equals(""))
            {
                LastLoginIP = temp.Split(';')[0];
                LastSelectedIndex = Convert.ToInt32(temp.Split(';')[1]);

                XmlHelper.ReadAllDatas(LastLoginIP, ref FtpData);
                if (FtpData.Count > 0)
                {
                    FillCombAcc();
                    setAcctInfo();
                }                   
            }

            //获取保存路径
            LocalSavePath = XmlHelper.getSavePath();
            if (LocalSavePath.Equals(""))
                LocalSavePath = "./FtpDownLoad";
            label7.Text = LocalSavePath;
        }

        /// <summary>
        /// 界面部分可否编辑控制
        /// </summary>
        private void EnableOrDis()
        {
            btnDisCon.Enabled = !btnDisCon.Enabled;
            btnCon.Enabled = !btnCon.Enabled;
            comboAcct.Enabled = btnCon.Enabled;
            textURL.Enabled = btnCon.Enabled;
            textPassword.Enabled = btnCon.Enabled;
            comboEncode.Enabled = btnCon.Enabled;
            listView1.Enabled = !btnCon.Enabled;
        }

        #endregion
        
        #region ---文件浏览器的操作---
        /// <summary>
        /// 获取文件信息
        /// </summary>
        /// <param name="line">读取的一条文件信息</param>
        private void _split(String line)
        {
            line = line.Replace("    ", " ");
            List<String> results = line.Split(' ').ToList();
            //去掉空项
            results.RemoveAll(n => n == "");
            DataModel mod = new DataModel();
            if (results[0].Contains("dr"))
            {
                mod.Type = 1;
                mod.Name = results[results.Count - 1];
                //mod.Size = Math.Ceiling(double.Parse(re[4]) / 1024);
                currentDirList.Add(mod);
            }
            else
            {
                mod.Type = 0;
                mod.Name = results[results.Count - 1];
                mod.Size = Math.Ceiling(double.Parse(results[4]));
                currentFileList.Add(mod);
            }
        }

        /// <summary>
        /// 向列表添加信息
        /// </summary>
        public void _PrintcurrentFileListAndcurrentDirList()
        {
            listView1.Items.Clear();
            if (!currentUrl.Equals(RootUrl + "/"))
            {
                //此处的”\\..“修改时也要同时修改进入文件夹操作的
                listView1.Items.Add("\\..", 0);
            }
            foreach (DataModel dm in currentDirList)
            {
                ListViewItem itm = new ListViewItem();
                itm.Text = dm.Name;
                itm.SubItems.Add("");
                itm.SubItems.Add("文件夹");
                itm.ImageIndex = 0;
                listView1.Items.Add(itm);
            }
            foreach (DataModel dm in currentFileList)
            {
                ListViewItem itm = new ListViewItem();
                itm.Text = dm.Name;
                itm.SubItems.Add(dm.Size + dm.Unit);
                itm.SubItems.Add("文件");
                itm.ImageIndex = 1;
                listView1.Items.Add(itm);
            }
        }

        /// <summary>
        /// 变换当前路径
        /// </summary>
        /// <param name="str">当前路径</param>
        private void Change_currentPath(String str)
        {
            currentUrl = str;
            currentPath.Text = "当前路径：" + str;
        }

        /// <summary>
        /// 进入文件夹
        /// </summary>
        /// <param name="path">路径名</param>
        private void enterADirectury(String path)
        {
             currentDirList.Clear();
             currentFileList.Clear();
             String url = path;
             FtpWebRequest FWReq = (FtpWebRequest)WebRequest.Create(url);
             FWReq.Credentials = new NetworkCredential(user, pass);
             if(USESSL)
             {
                 FWReq.EnableSsl = true;
                 ServicePointManager.ServerCertificateValidationCallback = 
                     new RemoteCertificateValidationCallback(ValidateServerCertificate);
             }
             FWReq.Timeout = 5000;
             FWReq.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
             FtpWebResponse FWRes = (FtpWebResponse)FWReq.GetResponse();
             StreamReader reader = new StreamReader(FWRes.GetResponseStream(), Encoding.UTF8);
             String line = reader.ReadLine();
             //更改当前路径
             Change_currentPath(FWRes.ResponseUri.AbsoluteUri);
             while (line != null)
             {
                 _split(line);
                 line = reader.ReadLine();
             }
             _PrintcurrentFileListAndcurrentDirList();
             reader.Close();
             FWRes.Close();
        }        

        /// <summary>
        /// ssl加密
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="certificate"></param>
        /// <param name="chain"></param>
        /// <param name="sslPolicyErrors"></param>
        /// <returns></returns>
        public static bool ValidateServerCertificate
         (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
        #endregion                              

        #region ---窗体界面操作---
        /// <summary>
        /// 打开连接
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button1_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(comboAcct.Text.ToString()) 
                || string.IsNullOrWhiteSpace(textPassword.Text) 
                || string.IsNullOrWhiteSpace(textURL.Text)
                )
            {
                MessageBox.Show("输入完整信息！");
                return;
            }
            try
            {
                String url = "ftp://" + textURL.Text.Trim();               
                RootUrl = url;
                user = comboAcct.Text.ToString();
                pass = textPassword.Text.ToString();              
                enterADirectury(url);
                EnableOrDis();
                 
                //存储数据
                saveOneData();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

        }

        /// <summary>
        /// 模拟文件浏览器操作
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listView1_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                String name = listView1.Items[listView1.SelectedIndices[0]].SubItems[0].Text;
                if (name.Equals("\\.."))
                {
                    String uu = currentUrl.Substring(0, currentUrl.LastIndexOf('/'));
                    enterADirectury(uu);
                    return;
                }
                String type = listView1.Items[listView1.SelectedIndices[0]].SubItems[2].Text;
                if (type.Trim().Equals("文件夹"))
                {
                    if(currentUrl.ElementAt(currentUrl.Length - 1) == '/')
                    {
                        enterADirectury(currentUrl + name);
                    }
                    else
                    {
                        enterADirectury(currentUrl + "/"+ name);
                    }
                    return;
                }
                else
                {
                    DialogResult result = MessageBox.Show("你选中了文件：" + name + "\n是否要下载？", "下载提示", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk);
                    if (result == System.Windows.Forms.DialogResult.Yes)
                    {
                        if(downloads.IsAlive)
                        {
                            MessageBox.Show("请等待当前文件下载完！");
                            return;
                        }
                        else
                        {
                            downloads = new Thread(DownloadFile);
                            downloads.Start();
                        }                          
                        return;
                    }
                    else
                    {

                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }           
        }

        /// <summary>
        /// 假装退出程序
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button4_Click(object sender, EventArgs e)
        {
            try
            {
                EnableOrDis();
                if(uploads.IsAlive)
                {
                    uploads.Abort();
                }                   
                if(downloads.IsAlive)
                {
                    downloads.Abort();
                }                   
            }
            catch (Exception ex)
            {
                //MessageBox.Show(ex.Message);
            }           
        }

        /// <summary>
        /// 修改progressBar1的值（下载）
        /// </summary>
        /// <param name="va">要修改的值</param>
        private void ChangeP1(int va)
        {
            progressBar1.Value = va;
            label1.Text = "下载进度：" + va + "%";
        }

        /// <summary>
        /// 修改progressBar2的值(上传)
        /// </summary>
        /// <param name="va">要修改的值</param>
        private void ChangeP2(int va)
        {
            progressBar2.Value = va;
            label6.Text = "上传进度：" + va + "%";
        }

        /// <summary>
        /// 针对ListView1的访问
        /// </summary>
        /// <returns></returns>
        private String GetListName()
        {
            return listView1.Items[listView1.SelectedIndices[0]].SubItems[0].Text;
        }

        private String GetListType()
        {
            return listView1.Items[listView1.SelectedIndices[0]].SubItems[2].Text;
        }
        #endregion                  

        #region ---右键菜单操作---        
        /// <summary>
        /// 上传文件的方法
        /// </summary>
        private void UploadFile()
        {
            openFileDialog1 = new OpenFileDialog();
            String UpFilePath = "";
            String name = "";
            FileInfo fileInfo = null;
            FileStream input = null;
            FtpWebRequest ftprequest = null;
            String realPath;
            try
            {
                //if (!Directory.Exists(@"D:/MyFTP/Download/"))
                //{
                //    //创建路径
                //    Directory.CreateDirectory(@"D:/MyFTP/Download/");
                //}
                //openFileDialog1.InitialDirectory = @"D:/MyFTP/Download/";
                openFileDialog1.Title = "选择要上传的文件";
                if (openFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    UpFilePath = openFileDialog1.FileName;
                    name = UpFilePath.Substring(UpFilePath.LastIndexOf('\\') + 1);
                    fileInfo = new FileInfo(@UpFilePath);

                    //设置相关上传属性
                    input = new FileStream(@UpFilePath, FileMode.Open);
                    
                    //路径拼接
                    if (currentUrl.ElementAt(currentUrl.Length - 1) == '/')
                        realPath = currentUrl + name;
                    else
                        realPath = currentUrl + "/" + name;
                    
                    ftprequest = (FtpWebRequest)WebRequest.Create(@realPath);
                    ftprequest.UseBinary = true;
                    ftprequest.Timeout = 5000;
                    ftprequest.Credentials = new NetworkCredential(user, pass);
                    if (USESSL)
                    {
                        ftprequest.EnableSsl = true;
                        ServicePointManager.ServerCertificateValidationCallback =
                            new RemoteCertificateValidationCallback(ValidateServerCertificate);
                    }
                    ftprequest.Method = WebRequestMethods.Ftp.UploadFile;

                    //设置显示相关
                    double total = fileInfo.Length;
                    double current = 0;                   
                    Stream writer = ftprequest.GetRequestStream();
                    byte[] buffer = new byte[2048];
                    int leng = 0;
                    //**************************************************
                    timer_upload.Start();

                    while ((leng = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        current += leng;

                        //*******************************
                        tick2 += leng;
                        writer.Write(buffer, 0, leng);
                        double real = (current / total) * 100;
                        int prog = (int)Math.Ceiling(real);
                        if (prog % 1 == 0)
                            this.Invoke(changep2, prog);
                        //不加这句，label将会来不及显示
                        System.Windows.Forms.Application.DoEvents();
                    }
                    input.Close();
                    input.Dispose();
                    writer.Close();
                    writer.Dispose();

                    //进入Upload文件夹
                    enterADirectury(currentUrl);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("上传失败！\n\t请检查是否有权限在当前目录下执行上传操作！");
            }
            finally
            {
                timer_upload.Stop();
                tick2 = 0;
            }       
        }

        /// <summary>
        /// 下载文件的方法
        /// </summary>
        private void DownloadFile()
        {           
            try
            {
                String fileName = this.Invoke(getName).ToString();
                String type = this.Invoke(getType).ToString();
                String realPath;               
                if (!type.Equals("文件"))
                {
                    MessageBox.Show("请选择要下载的文件！");
                    return;
                }
                
                //判断是否有文件夹
                if(!Directory.Exists(@LocalSavePath))
                {
                    Directory.CreateDirectory(@LocalSavePath);
                }
                
                //获取文件流
                FileStream output = new FileStream(@LocalSavePath + "/" + fileName, FileMode.OpenOrCreate);
                
                //路径拼接
                if(currentUrl.ElementAt(currentUrl.Length - 1) == '/')
                    realPath = currentUrl + fileName;
                else
                    realPath = currentUrl + "/" +fileName;
                
                //获取服务器文件
                FtpWebRequest ftpRequest = (FtpWebRequest)WebRequest.Create(@realPath);
                ftpRequest.UseBinary = true;               
                ftpRequest.Timeout = 5000;
                ftpRequest.Credentials = new NetworkCredential(user, pass);
                if (USESSL)
                {
                    ftpRequest.EnableSsl = true;
                    ServicePointManager.ServerCertificateValidationCallback =
                        new RemoteCertificateValidationCallback(ValidateServerCertificate);
                }
                ftpRequest.Method = WebRequestMethods.Ftp.DownloadFile;
                FtpWebResponse ftpResponse = (FtpWebResponse)ftpRequest.GetResponse();
                
                //设置显示相关
                double total = GetFileSize(realPath);
                double current = 0;                
                Stream reader = ftpResponse.GetResponseStream();
                byte[] buffer = new byte[2048];
                int leng = 0;

                //开始计时*************************************************
                timer_download.Start();

                while ((leng = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    current += leng;
                    //*****************************************************
                    tick1 += leng;
                    
                    output.Write(buffer, 0, leng);
                    double real = (current / total) * 100;
                    int prog = (int)Math.Ceiling(real);
                    if (prog % 1 == 0)
                        this.Invoke(changep1, prog);                  
                    //不加这句，label将会来不及显示
                    System.Windows.Forms.Application.DoEvents();       
                }
                output.Close();
                output.Dispose();
                reader.Close();
                reader.Dispose();
                ftpResponse.Close();
                ftpResponse.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show("下载失败！\n\t请检查是否有权限在当前目录下执行下载操作！");
            }
            finally
            {
                //清除
                timer_download.Stop();
                tick1 = 0;
            }
        }

        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toolUpload_Click(object sender, EventArgs e)
        {
            timer_upload.Enabled = true;
            timer_upload.Interval = 1000;
            UploadFile();
        }

        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toolDownload_Click(object sender, EventArgs e)
        {
            timer_download.Enabled = true;
            timer_download.Interval = 1000;
            
            progressBar1.Maximum = 100;
            progressBar1.Value = 0;
            if(downloads.IsAlive)
            {
                MessageBox.Show("请等待当前下载！");
                return;
            }
            else
            {
                downloads = new Thread(DownloadFile);
                downloads.Start();
            }
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toolDelete_Click(object sender, EventArgs e)
        {
            try
            {
                String fileName = this.Invoke(getName).ToString();
                String type = this.Invoke(getType).ToString();
                String realPath;
                //路径拼接
                if (currentUrl.ElementAt(currentUrl.Length - 1) == '/')
                    realPath = currentUrl + fileName;
                else
                    realPath = currentUrl + "/" + fileName;

                FtpWebRequest ftpRequest = (FtpWebRequest)WebRequest.Create(@realPath);
                //ftpRequest.UseBinary = true;
                ftpRequest.Timeout = 5000;
                ftpRequest.Credentials = new NetworkCredential(user, pass);
                if (USESSL)
                {
                    ftpRequest.EnableSsl = true;
                    ServicePointManager.ServerCertificateValidationCallback =
                        new RemoteCertificateValidationCallback(ValidateServerCertificate);
                }
                ftpRequest.Method = WebRequestMethods.Ftp.DeleteFile;
                FtpWebResponse ftpResponse = (FtpWebResponse)ftpRequest.GetResponse();
                
                //刷新当前文件夹
                enterADirectury(currentUrl);
                ftpResponse.Close();
                ftpResponse.Dispose();
            }
            catch (Exception ex)
            {
                //MessageBox.Show("删除失败！\n\t请检查是否有权限在当前目录下执行删除操作！");
                MessageBox.Show(ex.Message);
            }
        }
        #endregion              
       
        #region ---其他操作---
        /// <summary>
        /// 获取文件大小
        /// </summary>
        /// <param name="str">文件路径</param>
        /// <returns></returns>
        private double GetFileSize(String str)
        {
            FtpWebRequest ftpRequest = (FtpWebRequest)WebRequest.Create(@str);
            ftpRequest.UseBinary = true;
            ftpRequest.UseBinary = true;
            ftpRequest.Timeout = 5000;
            ftpRequest.Credentials = new NetworkCredential(user, pass);
            if (USESSL)
            {
                ftpRequest.EnableSsl = true;
                ServicePointManager.ServerCertificateValidationCallback =
                    new RemoteCertificateValidationCallback(ValidateServerCertificate);
            }
            ftpRequest.Method = WebRequestMethods.Ftp.GetFileSize;

            FtpWebResponse res = (FtpWebResponse)ftpRequest.GetResponse();
            double length = res.ContentLength;
            res.Close();
            res.Dispose();
            return length;
        }

        /// <summary>
        /// 是否启用ssl
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void comboEncode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboEncode.SelectedIndex == 1)
                USESSL = true;
            else
                USESSL = false;
        }

        /// <summary>
        /// 切换显示方式
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button1_Click_1(object sender, EventArgs e)
        {
            listView1.View = (listView1.View == View.Details) ? View.LargeIcon : View.Details;
            button1.Text = (listView1.View == View.Details) ? "列表显示" : "图标显示";
        }

        /// <summary>
        /// 设置保存路径
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button2_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1 = new FolderBrowserDialog();
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                //MessageBox.Show(folderBrowserDialog1.SelectedPath);
                LocalSavePath = folderBrowserDialog1.SelectedPath.Replace('\\', '/');
                XmlHelper.setSavePath(LocalSavePath);

                label7.Text = LocalSavePath;
            }
        }

        /// <summary>
        /// 计时器下载
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void timer_download_Tick(object sender, EventArgs e)
        {
            if (tick1 >= 1024)
            {
                tick1 /= 1024;
                label8.Text = String.Format("当前下载速度：{0:##.#}K/s", tick1);
            }
            if (tick1 >= 1024)
            {
                tick1 /= 1024;
                label8.Text = String.Format("当前下载速度：{0:##.#}M/s", tick1);
            }
            tick1 = 0;
        }

        /// <summary>
        /// 计时器上传
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void timer_upload_Tick(object sender, EventArgs e)
        {
            if (tick2 >= 1024)
            {
                tick2 /= 1024;
                label9.Text = String.Format("当前上传速度：{0:##.#}K/s", tick2);
            }
            if (tick2 >= 1024)
            {
                tick2 /= 1024;
                label9.Text = String.Format("当前上传速度：{0:##.#}M/s", tick2);
            }
            tick2 = 0;
        }
        #endregion

        #region ---账号密码操作---
        /// <summary>
        /// 填充数据
        /// </summary>
        private void setAcctInfo()
        {
            textURL.Text = LastLoginIP;
            //触发 comboAcct_SelectedIndexChanged
            comboAcct.SelectedIndex = LastSelectedIndex;
        }

        /// <summary>
        /// 账号下拉列表填充
        /// </summary>
        private void FillCombAcc()
        {
            //清空
            comboAcct.Items.Clear();
            foreach(A_PS ap in FtpData)
            {
                comboAcct.Items.Add(ap.ACCT);
            }
        }

        /// <summary>
        /// 填充密码
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void comboAcct_SelectedIndexChanged(object sender, EventArgs e)
        {
            comboAcct.Text = comboAcct.SelectedItem.ToString();
            textPassword.Text = FtpData[comboAcct.SelectedIndex].PASS;
        }

        /// <summary>
        /// 存储一条数据
        /// </summary>
        private void saveOneData()
        {
            A_PS temp = new A_PS() { ACCT = comboAcct.Text, PASS = textPassword.Text };
            XmlHelper.AddOneData(textURL.Text, temp.ACCT, temp.PASS);

            if(textURL.Text.Equals(LastLoginIP))
            {
                if (!FtpData.Contains(temp))
                {
                    comboAcct.Items.Add(temp.ACCT);
                    FtpData.Add(temp);                   
                }
            }
            else
            {
                XmlHelper.ReadAllDatas(textURL.Text, ref FtpData);
                FillCombAcc();
                LastSelectedIndex = FtpData.Count - 1;
                LastLoginIP = textURL.Text;
                XmlHelper.setLastChoice(LastLoginIP, LastSelectedIndex);
                
                //填充数据
                //setAcctInfo();
            }
        }

        /// <summary>
        /// 输入完后自动填充密码
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void textURL_Leave(object sender, EventArgs e)
        {
            XmlHelper.ReadAllDatas(textURL.Text, ref FtpData);
            if(FtpData.Count > 0)
            {
                LastLoginIP = textURL.Text;
                FillCombAcc();
                setAcctInfo();
            }
        }
        #endregion                             
        
    }
}
