using System;
using System.IO;
using System.Windows;

namespace KillerPDF.Services.Signing
{
    /// <summary>
    /// TEMPORARY dev-only smoke test for digital signing. Delete this file (and its Ctrl+Shift+F12
    /// hook in MainWindow.OnPreviewKeyDown) before release. Signs the given PDF with the self-signed
    /// test certificate on the Desktop and writes "signed-output.pdf" next to it, then reports.
    /// </summary>
    internal static class SigningSmokeTest
    {
        public static void RunOnFile(string? inputPdfPath)
        {
            try
            {
                if (string.IsNullOrEmpty(inputPdfPath) || !File.Exists(inputPdfPath))
                {
                    MessageBox.Show("Open a PDF first, then press Ctrl+Shift+F12.", "Sign test");
                    return;
                }

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string pfx     = Path.Combine(desktop, "killerpdf-test.pfx");
                string output  = Path.Combine(desktop, "signed-output.pdf");

                if (!File.Exists(pfx))
                {
                    MessageBox.Show($"Test certificate not found:\n{pfx}\n\nRun the New-SelfSignedCertificate PowerShell first.", "Sign test");
                    return;
                }

                var cert = new PfxFileCertificateProvider(pfx, "test1234").GetCertificate();
                new PdfSigner().Sign(inputPdfPath!, output, cert,
                    new PdfSigner.SignInfo("Testing", "Test City", "test@example.com"));

                MessageBox.Show(
                    $"Signed OK.\n\nOutput:\n{output}\n\nOpen it in Adobe Reader. A self-signed cert shows " +
                    "\"identity unknown\" - that is expected. What matters is that a signature exists and the " +
                    "document is reported \"not modified since signed\".",
                    "Sign test");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Sign test FAILED:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex}", "Sign test");
            }
        }
    }
}
