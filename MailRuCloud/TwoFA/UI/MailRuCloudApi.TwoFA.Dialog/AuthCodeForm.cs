﻿using System.Windows.Forms;

namespace YaR.Clouds.MailRuCloud.TwoFA.UI
{
    public partial class AuthCodeForm : Form
    {
        public AuthCodeForm()
        {
            InitializeComponent();
        }

        public AuthDialogResult ShowAuthDialog(string login, bool isRelogin)
        {
            txtLogin.Text = login;
            lblInfo.Text = isRelogin
                ? "Auto relogin request"
                : "Login request";
            txtAuthCode.Focus();

            var res = ShowDialog();

            return new AuthDialogResult
            {
                DialogResult = res,
                AuthCode = txtAuthCode.Text
            };
        }

    }

    public class AuthDialogResult
    {
        public DialogResult DialogResult { get; set; }
        public string AuthCode { get; set; }
    }
}
