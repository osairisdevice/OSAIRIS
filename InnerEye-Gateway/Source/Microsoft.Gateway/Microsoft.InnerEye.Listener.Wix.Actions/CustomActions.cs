// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Microsoft.InnerEye.Listener.Wix.Actions
{
    using System;
#if DEBUG
    using System.Diagnostics;
#endif
    using System.IO;
    using System.Net;
    using System.Security.Authentication;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    using Microsoft.Deployment.WindowsInstaller;
    using Microsoft.InnerEye.Listener.Common.Providers;

    /// <summary>
    /// The collection of custom actions run by the WiX installer.
    /// </summary>
    public static class CustomActions
    {
        /// <summary>
        /// The command line argument to silent install.
        /// </summary>
        private const string UILevelCustomActionKey = "UILevel";

        /// <summary>
        /// Gets the install path.
        /// </summary>
        /// <value>
        /// The install path.
        /// </value>
        private static string InstallPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft InnerEye Gateway");

        /// <summary>
        /// Gets the config folder path.
        /// </summary>
        /// <value>
        /// The config folder path.
        /// </value>
        private static string ConfigInstallDirectory => Path.Combine(InstallPath, "Config");

        /// <summary>
        /// The pre-install custom action.
        /// Asks the user for a license-key and validates it before continuing with the install.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns>The action result.</returns>
        [CustomAction]
        public static ActionResult ValidateProductKey(Session session)
        {
#if DEBUG
            Debugger.Launch();
#endif

            // Make sure that the applications are run as services.
            using (var gatewayProcessorConfigProvider = new GatewayProcessorConfigProvider(null, ConfigInstallDirectory))
            {
                gatewayProcessorConfigProvider.SetRunAsConsole(false);

                using (var gatewayReceiveConfigProvider = new GatewayReceiveConfigProvider(
                    null,
                    ConfigInstallDirectory))
                {
                    gatewayReceiveConfigProvider.SetRunAsConsole(false);
                }

                // Check if the installer is running unattended - lets skip the UI if true
                if (session.CustomActionData[UILevelCustomActionKey] == "2")
                {
                    return ActionResult.Success;
                }

                // In the context of the installer, this may have a different SecurityProtocol to the application.
                // In testing it was: SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls
                // but it may vary. In order to value the uri and license key, we need TLS 1.2
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                // First time install so lets display a form to grab the license key.
                using (var form = new LicenseKeyForm(gatewayProcessorConfigProvider))
                {
                    var licenseKeyDialogResult = form.ShowDialog();

                    switch (licenseKeyDialogResult)
                    {
                        case DialogResult.Cancel:
                            return ActionResult.UserExit;
                        case DialogResult.No:
                            return ActionResult.NotExecuted;
                        default:
                            return ActionResult.Success;
                    }
                }
            }
        }

        /// <summary>
        /// Validates the license key using the InnerEye segmentation client.
        /// </summary>
        /// <param name="gatewayProcessorConfigProvider">Gateway processor config provider.</param>
        /// <param name="licenseKey">The license key to validate.</param>
        /// <param name="inferenceUri">Inference Uri to validate.</param>
        /// <returns>If valid and text to display with the validation result.</returns>
        internal static async Task<(bool Result, string ValidationText)> ValidateLicenseKeyAsync(GatewayProcessorConfigProvider gatewayProcessorConfigProvider, string licenseKey, Uri inferenceUri)
        {
            var validationText = string.Empty;
            var processorSettings = gatewayProcessorConfigProvider.ProcessorSettings();
            var existingInferenceUri = processorSettings.InferenceUri;
            var existingLicenseKey = processorSettings.LicenseKey;

            try
            {
                // Update the settings for the Gateway.
                gatewayProcessorConfigProvider.SetProcessorSettings(inferenceUri, licenseKey);

                using (var segmentationClient = gatewayProcessorConfigProvider.CreateInnerEyeSegmentationClient()())
                {
                    await segmentationClient.PingAsync();
                }

                return (true, validationText);
            }
            catch (AuthenticationException)
            {
                validationText = "Invalid product key";
            }
            catch (Exception)
            {
                validationText = "Unable to connect to inference service uri";
            }

            // Restore the previous config
            gatewayProcessorConfigProvider.SetProcessorSettings(existingInferenceUri, existingLicenseKey);

            return (false, validationText);
        }
    }
}
