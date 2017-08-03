using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Babel.Licensing;

namespace LicenseInjector.Code
{
    public class LicenseFileCheckWinForm
    {
        static LicenseFileCheckWinForm()
        {
            BabelLicenseManager.RegisterLicenseProvider(typeof(LicenseFileCheckWinForm), new FileLicenseProvider());
        }

        public static void ValidateLicense()
        {
            try
            {
                ILicense license = BabelLicenseManager.Validate(typeof(LicenseFileCheckWinForm), new LicenseFileCheckWinForm());
                StoreLicenseInfo(license);
            }
            catch (Exception ex)
            {
                OnLicenseValidationFailed(ex.Message);
            }
        }

        public static void StoreLicenseInfo(ILicense license)
        {
            var domain = AppDomain.CurrentDomain;

            if (!string.IsNullOrEmpty(license.Type))
                domain.SetData("LicenseType", license.Type);

            if (license.IssueDate.HasValue)
                domain.SetData("LicenseIssueDate", license.IssueDate.Value);

            if (license.ExpireDate.HasValue)
                domain.SetData("LicenseExpireDate", license.ExpireDate.Value);

            // TODO: Store additional license information like fields, features, restrictions            


            // TODO: Use the following code inside your application to 
            // process the information stored inside the license file

            // var domain = AppDomain.CurrentDomain;
            // string licenseType = (string)domain.GetData("LicenseType") ?? string.Empty;
            // DateTime? issueDate = (DateTime?)domain.GetData("LicenseIssueDate");
            // DateTime? expireDate = (DateTime?)domain.GetData("LicenseExpireDate");            
        }

        public static void OnLicenseValidationFailed(string reason)
        {
            string message = string.Format("License error: {0}", reason);
            MessageBox.Show(message, Application.ProductName, MessageBoxButtons.OK);
            Environment.FailFast(message);
        }
    }
}
