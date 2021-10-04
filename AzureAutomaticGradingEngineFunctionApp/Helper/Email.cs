using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Text;

namespace AzureAutomaticGradingEngineFunctionApp.Helper
{
    public class Email
    {
        private readonly SmtpClient client
            ;

        public Email()
        {
            //https://stackoverflow.com/questions/18503333/the-smtp-server-requires-a-secure-connection-or-the-client-was-not-authenticated

            client = new SmtpClient("smtp.gmail.com", 587);
            client.EnableSsl = true;
            client.UseDefaultCredentials = false;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.Credentials = new System.Net.NetworkCredential("azureautomaticgrader@gmail.com", "it114115cdca");

        }

        public void Send(string to, string subject, string body)
        {
            MailMessage message = new MailMessage("azureautomaticgrader@gmail.com", to, subject, body);
            client.Send(message);
        }
    }
}
