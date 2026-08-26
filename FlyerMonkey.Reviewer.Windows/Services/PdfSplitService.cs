using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using System.IO;

namespace FlyerMonkey.Reviewer.Windows.Services;

public class PdfSplitService
{
    public List<byte[]> SplitPdf(Stream pdfStream)
    {
        var pages = new List<byte[]>();

        using var loadedDocument =
            new PdfLoadedDocument(pdfStream, true);

        for (int i = 0; i < loadedDocument.Pages.Count; i++)
        {
            using var document = new PdfDocument();

            document.ImportPage(loadedDocument, i);

            using var stream = new MemoryStream();
            document.Save(stream);

            pages.Add(stream.ToArray());
        }

        return pages;
    }
}