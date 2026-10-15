namespace Mark2;

using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using NaturalSort.Extension;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Spreadsheet = DocumentFormat.OpenXml.Spreadsheet;

public class Survey
{
    public string? Title { get; set; }
    public List<RepositoryPayload> RepositoryPayloads { get; set; } = [];

    public IList<IBrowserFile> ImageFiles { get; set; }
    public double AreaThreshold { get; set; }
    public double ColorThreshold { get; set; }
    public List<Page> Pages { get; set; }

    public Dictionary<string, List<List<int>>> Answers { get; set; }
    public string SelectedLogImage { get; set; }

    public Survey()
    {
        ImageFiles = new List<IBrowserFile>();
        Pages = [];
        Answers = [];
        SelectedLogImage = string.Empty;
    }

    public async Task FetchRepository(string surveyId)
    {
        HttpClient client = new();
        string url = "https://repository.mark2.org/" + surveyId + ".json";

        var repository = await client.GetFromJsonAsync<Repository>(url);
        if (repository != null)
        {
            Title = repository.Name;
            RepositoryPayloads = repository.Payloads!;
        }
    }

    public void SetupPositionsFromRepository(string? repositoryPayloadName)
    {
        foreach (var payload in RepositoryPayloads)
        {
            if (payload.Name == repositoryPayloadName && payload.Values != null)
            {
                Pages = [];
                var row = payload.Values[0];

                List<int> vs = [];
                for (int i = 1; i <= row.Count / 4; i++)
                {
                    try
                    {
                        vs.Add(row[i * 4].GetInt32());
                    }
                    catch (Exception)
                    {
                    }
                }

                for (int i = 3; i < payload.Values.Count; i++)
                {
                    row = payload.Values[i];
                    int pageNumber = row[2].GetInt32();
                    while (Pages.Count < pageNumber)
                    {
                        Pages.Add(new Page());
                    }

                    Question question = new()
                    {
                        Text = row[1].ToString(),
                        Type = row[3].GetInt32()
                    };

                    for (int j = 1; j <= row.Count / 4; j++)
                    {
                        try
                        {
                            Area area = new()
                            {
                                X = row[j * 4].GetInt32(),
                                Y = row[j * 4 + 1].GetInt32(),
                                Width = row[j * 4 + 2].GetInt32(),
                                Height = row[j * 4 + 3].GetInt32(),
                                Value = vs[j - 1]
                            };

                            question.Areas.Add(area);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    Pages[pageNumber - 1].Questions.Add(question);
                }
            }
        }
    }

    public void SetupPositionsFromFile(MemoryStream ms)
    {
        Pages = [];
        using var workbook = new XLWorkbook(ms);
        var sheet = workbook.Worksheet(1);
        var row = sheet.Row(1);

        List<int> vs = [];
        for (int i = 1; i <= LastCellNum(row) / 4; i++)
        {
            try
            {
                vs.Add(NumericCellValue(GetCell(row, i * 4)));
            }
            catch (Exception)
            {
            }
        }

        for (int i = 3; i <= LastRowNum(sheet); i++)
        {
            int pageNumber;
            Question question;

            try
            {
                row = sheet.Row(i + 1);
                pageNumber = NumericCellValue(GetCell(row, 2));
                while (Pages.Count < pageNumber)
                {
                    Pages.Add(new Page());
                }

                question = new()
                {
                    Text = StringCellValue(GetCell(row, 1)),
                    Type = NumericCellValue(GetCell(row, 3))
                };
            }
            catch (Exception)
            {
                continue;
            }

            for (int j = 1; j <= LastCellNum(row) / 4; j++)
            {
                try
                {
                    Area area = new()
                    {
                        X = NumericCellValue(GetCell(row, j * 4)),
                        Y = NumericCellValue(GetCell(row, j * 4 + 1)),
                        Width = NumericCellValue(GetCell(row, j * 4 + 2)),
                        Height = NumericCellValue(GetCell(row, j * 4 + 3)),
                        Value = vs[j - 1]
                    };

                    question.Areas.Add(area);
                }
                catch (Exception)
                {
                }
            }
            Pages[pageNumber - 1].Questions.Add(question);
        }
    }

    // Helpers that keep the 0-based indices and the error behavior of the former NPOI code:
    // reading an empty cell or a non-numeric cell as a number throws, and callers skip it.
    private static IXLCell GetCell(IXLRow row, int columnIndex) => row.Cell(columnIndex + 1);

    private static int LastCellNum(IXLRow row) => row.LastCellUsed()?.Address.ColumnNumber ?? 0;

    private static int LastRowNum(IXLWorksheet sheet) => (sheet.LastRowUsed()?.RowNumber() ?? 0) - 1;

    private static XLCellValue CellValue(IXLCell cell)
    {
        var value = cell.HasFormula ? cell.CachedValue : cell.Value;
        if (value.IsBlank)
        {
            throw new InvalidOperationException($"Cell {cell.Address} is empty.");
        }
        return value;
    }

    private static int NumericCellValue(IXLCell cell) => Convert.ToInt32(CellValue(cell).GetNumber());

    private static string StringCellValue(IXLCell cell) => CellValue(cell).ToString(CultureInfo.CurrentCulture);

    public async Task Recognize(int index, IJSRuntime js, bool updateLogImage = false)
    {
        bool imageLoaded = true;
        MemoryStream stream = new();
        await ImageFiles[index].OpenReadStream(1024 * 1024 * 24).CopyToAsync(stream);
        var image = new Image<Rgba32>(595, 842);
        try
        {
            image = Image.Load<Rgba32>(stream.ToArray());
        }
        catch
        {
            imageLoaded = false;
        }

        Item item = new(index, Pages[index % Pages.Count], ColorThreshold, AreaThreshold,
                        ImageFiles[index].Name, image, js);
        if (imageLoaded)
        {
            try
            {
                await item.Recognize();
                Answers[item.Name] = item.Answers;
                if (updateLogImage)
                {
                    SelectedLogImage = item.LogImageBase64();
                }
            }
            catch
            {
                Answers[item.Name] = item.ErrorAnswers();
                if (updateLogImage)
                {
                    SelectedLogImage = item.ErrorImageBase64();
                }
            }
        }
        else
        {
            Answers[item.Name] = item.ErrorAnswers();
            if (updateLogImage)
            {
                SelectedLogImage = item.ErrorImageBase64();
            }
        }
    }

    public string ResultSpreadsheet()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");

        // first header
        var row = sheet.Row(1);
        GetCell(row, 0).Value = "No";
        GetCell(row, 1).Value = "File";
        int questionIndex = 0;
        foreach (var page in Pages)
        {
            foreach (var question in page.Questions)
            {
                GetCell(row, questionIndex + 2).Value = questionIndex + 1;
                questionIndex++;
            }
        }

        // second header
        row = sheet.Row(2);
        questionIndex = 0;
        foreach (var page in Pages)
        {
            foreach (var question in page.Questions)
            {
                GetCell(row, questionIndex + 2).Value = question.Text;
                questionIndex++;
            }
        }

        int rowIndex = 1;
        int itemIndex = 0;
        List<string> names = [];
        foreach (var _answers in Answers.OrderBy(d => d.Key, StringComparison.OrdinalIgnoreCase.WithNaturalSort()))
        {
            // first page
            if (itemIndex % Pages.Count == 0)
            {
                rowIndex++;
                row = sheet.Row(rowIndex + 1);
                GetCell(row, 0).Value = rowIndex - 1;
                questionIndex = 2;
                names = [];
            }

            names.Add(_answers.Key);
            foreach (var answer in _answers.Value)
            {
                var cell = GetCell(row, questionIndex);
                if (answer.Count == 0)
                {
                    cell.Style.Fill.BackgroundColor = Gold;
                }
                else if (answer.Count == 1)
                {
                    cell.Value = answer[0];
                }
                else if (answer.Count > 1)
                {
                    cell.Style.Fill.BackgroundColor = Coral;

                    var cellValue = string.Join(";", answer);
                    if (cellValue != "-999;-999")
                    {
                        cell.Value = cellValue;
                    }
                }

                questionIndex++;
            }

            // last page
            if ((itemIndex + 1) % Pages.Count == 0 || itemIndex + 1 == Answers.Count)
            {
                GetCell(row, 1).Value = string.Join(";", names);
            }

            itemIndex++;
        }

        MemoryStream stream = new();
        workbook.SaveAs(stream);
        if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja")
        {
            UseJapaneseDefaultFont(stream);
        }

        return Convert.ToBase64String(stream.ToArray());
    }

    // ClosedXML always writes Calibri as the default font, so replace it with the default font of Japanese Excel
    private static void UseJapaneseDefaultFont(Stream stream)
    {
        using var document = SpreadsheetDocument.Open(stream, true);
        var workbookPart = document.WorkbookPart!;

        // Excel takes the font of the minor scheme from the theme, which ClosedXML sets to ＭＳ Ｐゴシック for Japanese
        var fontScheme = workbookPart.ThemePart!.Theme!.ThemeElements!.FontScheme!;
        SetJapaneseThemeFont(fontScheme.MajorFont!, "游ゴシック Light");
        SetJapaneseThemeFont(fontScheme.MinorFont!, "游ゴシック");

        var font = workbookPart.WorkbookStylesPart!.Stylesheet!.Fonts!.Elements<Spreadsheet.Font>().First();
        font.FontName = new Spreadsheet.FontName { Val = "游ゴシック" };
        font.FontCharSet = new Spreadsheet.FontCharSet { Val = 128 };
        font.FontScheme = new Spreadsheet.FontScheme { Val = Spreadsheet.FontSchemeValues.Minor };
    }

    private static void SetJapaneseThemeFont(Drawing.FontCollectionType fonts, string typeface)
    {
        var font = fonts.Elements<Drawing.SupplementalFont>().FirstOrDefault(f => f.Script == "Jpan");
        if (font is null)
        {
            fonts.AppendChild(new Drawing.SupplementalFont { Script = "Jpan", Typeface = typeface });
        }
        else
        {
            font.Typeface = typeface;
        }
    }

    // Same indexed colors as NPOI's IndexedColors.Gold (51) and IndexedColors.Coral (29)
    private static readonly XLColor Gold = XLColor.FromIndex(51);
    private static readonly XLColor Coral = XLColor.FromIndex(29);
}
