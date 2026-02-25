using System;
using System.Drawing;
using System.Windows.Forms;

namespace WordTools
{
    /// <summary>
    /// 图标测试窗体 - 用于验证资源文件加载是否正常
    /// </summary>
    public partial class IconTestForm : Form
    {
        public IconTestForm()
        {
            InitializeComponent();
            LoadIconTests();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            
            // 
            // IconTestForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(600, 400);
            this.Text = "WordTools 图标测试";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ShowInTaskbar = false;
            
            this.ResumeLayout(false);
        }

        private void LoadIconTests()
        {
            try
            {
                // 清理现有控件
                this.Controls.Clear();

                // 添加标题
                var titleLabel = new Label
                {
                    Text = "WordTools 图标加载测试",
                    Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                    Location = new Point(10, 10),
                    Size = new Size(580, 30),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                this.Controls.Add(titleLabel);

                // 添加资源状态信息
                var statusText = new TextBox
                {
                    Text = ResourceManager.GetResourceStatus(),
                    Location = new Point(10, 50),
                    Size = new Size(580, 100),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical
                };
                this.Controls.Add(statusText);

                // 测试图标
                var iconNames = new[]
                {
                    "icons8-better-150.png",
                    "icons8-deepseek-150.png",
                    "icons8-spellcheck-70.png", 
                    "icons8-spellcheck-all-100.png",
                    "icons8-table_single-96.png",
                    "icons8-table_all-100.png",
                    "icons8-more-70.png",
                    "icons8-setting-128.png",
                    "icons8-clean-96.png"
                };

                int x = 10, y = 160;
                int iconSize = 48;
                
                foreach (var iconName in iconNames)
                {
                    try
                    {
                        // 加载图标
                        using (var originalImage = ResourceManager.GetIcon(iconName))
                        {
                            if (originalImage != null)
                            {
                                // 创建缩略图
                                var thumbnail = new Bitmap(originalImage, iconSize, iconSize);
                                
                                var pictureBox = new PictureBox
                                {
                                    Image = thumbnail,
                                    Location = new Point(x, y),
                                    Size = new Size(iconSize, iconSize),
                                    SizeMode = PictureBoxSizeMode.Zoom
                                };
                                
                                var label = new Label
                                {
                                    Text = iconName,
                                    Location = new Point(x, y + iconSize + 2),
                                    Size = new Size(iconSize + 20, 20),
                                    Font = new Font("Microsoft YaHei UI", 8F),
                                    TextAlign = ContentAlignment.TopCenter
                                };
                                
                                this.Controls.Add(pictureBox);
                                this.Controls.Add(label);
                                
                                // 测试IPictureDisp转换
                                var pictureDisp = ResourceManager.ImageToPictureDisp(originalImage);
                                System.Diagnostics.Debug.WriteLine($"[IconTest] {iconName}: IPictureDisp = {(pictureDisp != null ? "?" : "?")}");
                            }
                            else
                            {
                                var errorLabel = new Label
                                {
                                    Text = "?",
                                    ForeColor = Color.Red,
                                    Location = new Point(x, y),
                                    Size = new Size(iconSize, iconSize),
                                    TextAlign = ContentAlignment.MiddleCenter,
                                    Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold)
                                };
                                this.Controls.Add(errorLabel);
                                
                                var errorNameLabel = new Label
                                {
                                    Text = iconName + " (失败)",
                                    ForeColor = Color.Red,
                                    Location = new Point(x, y + iconSize + 2),
                                    Size = new Size(iconSize + 20, 20),
                                    Font = new Font("Microsoft YaHei UI", 8F),
                                    TextAlign = ContentAlignment.TopCenter
                                };
                                this.Controls.Add(errorNameLabel);
                            }
                        }
                        
                        x += iconSize + 30;
                        if (x > 500)
                        {
                            x = 10;
                            y += iconSize + 40;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IconTest] 加载图标 {iconName} 时出错: {ex.Message}");
                    }
                }

                // 添加关闭按钮
                var closeButton = new Button
                {
                    Text = "关闭",
                    Location = new Point(this.Width - 100, this.Height - 60),
                    Size = new Size(80, 30),
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Right
                };
                closeButton.Click += (s, e) => this.Close();
                this.Controls.Add(closeButton);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"图标测试初始化错误: {ex.Message}", "错误", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 显示图标测试窗体的静态方法
        /// </summary>
        public static void ShowTest()
        {
            try
            {
                var testForm = new IconTestForm();
                testForm.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法显示图标测试窗体: {ex.Message}", "错误", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}