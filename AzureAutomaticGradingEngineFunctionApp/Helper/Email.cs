using System.Net.Mail;
using AzureAutomaticGradingEngineFunctionApp.Model;
using Microsoft.Extensions.Logging;

namespace AzureAutomaticGradingEngineFunctionApp.Helper
{
    public class Email
    {
        private readonly ILogger _log;
        private readonly SmtpClient _client;
        private readonly string _fromAddress;

        public Email(Config config, ILogger log)
        {

            _log = log;
            //https://stackoverflow.com/questions/18503333/the-smtp-server-requires-a-secure-connection-or-the-client-was-not-authenticated

            var smtp = config.GetConfig(Config.Key.EmailSmtp);
            var loginName = config.GetConfig(Config.Key.EmailUserName);
            var password = config.GetConfig(Config.Key.EmailPassword);
            _fromAddress = config.GetConfig(Config.Key.EmailFromAddress);

            if (string.IsNullOrEmpty(smtp) || string.IsNullOrEmpty(loginName) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(_fromAddress))
            {
                _log.LogInformation("Missing SMTP Settings in App Settings!");
                return;
            }

            _client = new SmtpClient(smtp, 587);
            _client.EnableSsl = true;
            _client.UseDefaultCredentials = false;
            _client.DeliveryMethod = SmtpDeliveryMethod.Network;
            _client.Credentials = new System.Net.NetworkCredential(loginName, password);
        }

        public void Send(EmailMessage email, Attachment[] attachments)
        {
            if (_client == null)
            {
                _log.LogInformation("Skipped Missing SMTP Settings in App Settings: " + email.To);
                return;
            }
            var message = new MailMessage(_fromAddress, email.To, email.Subject, email.Body);

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    message.Attachments.Add(attachment);
                }
            }
            
            _client.Send(message);
            _log.LogInformation("Sent email to " + email.To);
        }
    }
}
