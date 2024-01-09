namespace Mark2;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using NaturalSort.Extension;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;

public class Survey
{
    public string? Title;
    public List<RepositoryPayload> RepositoryPayloads = [];

    public IList<IBrowserFile> ImageFiles;
    public double AreaThreshold;
    public double ColorThreshold;
    public List<Page> Pages;

    public Dictionary<string, List<List<int>>> Answers;
    public string SelectedLogImage;

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
                            Area area = new(row[j * 4].GetInt32(),
                                row[j * 4 + 1].GetInt32(),
                                row[j * 4 + 2].GetInt32(),
                                row[j * 4 + 3].GetInt32());
                            area.V = vs[j - 1];

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
        var workbook = new XSSFWorkbook(ms);
        var sheet = workbook.GetSheetAt(0);
        var row = sheet.GetRow(0);

        List<int> vs = [];
        for (int i = 1; i <= row.LastCellNum / 4; i++)
        {
            try
            {
                vs.Add(Convert.ToInt32(row.GetCell(i * 4).NumericCellValue));
            }
            catch (Exception)
            {
            }
        }

        for (int i = 3; i <= sheet.LastRowNum; i++)
        {
            int pageNumber;
            Question question;

            try
            {
                row = sheet.GetRow(i);
                pageNumber = Convert.ToInt32(row.GetCell(2).NumericCellValue);
                while (Pages.Count < pageNumber)
                {
                    Pages.Add(new Page());
                }

                question = new()
                {
                    Text = row.GetCell(1).ToString(),
                    Type = Convert.ToInt32(row.GetCell(3).NumericCellValue)
                };
            }
            catch (Exception)
            {
                continue;
            }

            for (int j = 1; j <= row.LastCellNum / 4; j++)
            {
                try
                {
                    Area area = new(Convert.ToInt32(row.GetCell(j * 4).NumericCellValue),
                        Convert.ToInt32(row.GetCell(j * 4 + 1).NumericCellValue),
                        Convert.ToInt32(row.GetCell(j * 4 + 2).NumericCellValue),
                        Convert.ToInt32(row.GetCell(j * 4 + 3).NumericCellValue));
                    area.V = vs[j - 1];

                    question.Areas.Add(area);
                }
                catch (Exception)
                {
                }
            }
            Pages[pageNumber - 1].Questions.Add(question);
        }
    }

    public async Task Recognize(int index, IJSRuntime js, bool updateLogImage = false)
    {
        MemoryStream stream = new();
        await ImageFiles[index].OpenReadStream(1024 * 1024 * 24).CopyToAsync(stream);
        var image = Image.Load<Rgba32>(stream.ToArray());
        Item item = new(index, Pages[index % Pages.Count], ColorThreshold, AreaThreshold,
                        ImageFiles[index].Name, image, js);
        await item.Recognize();
        Answers[item.Name] = item.Answers;
        if (updateLogImage)
        {
            SelectedLogImage = item.LogImageBase64();
        }
    }

    public string ResultSpreadsheet()
    {
        var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Sheet1");

        // first header
        var row = sheet.CreateRow(0);
        var cell = row.CreateCell(0);
        cell.SetCellValue("No");
        cell = row.CreateCell(1);
        cell.SetCellValue("File");
        int questionIndex = 0;
        foreach (var page in Pages)
        {
            foreach (var question in page.Questions)
            {
                cell = row.CreateCell(questionIndex + 2);
                cell.SetCellValue(questionIndex + 1);
                questionIndex++;
            }
        }

        // second header
        row = sheet.CreateRow(1);
        questionIndex = 0;
        foreach (var page in Pages)
        {
            foreach (var question in page.Questions)
            {
                cell = row.CreateCell(questionIndex + 2);
                cell.SetCellValue(question.Text);
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
                row = sheet.CreateRow(rowIndex);
                cell = row.CreateCell(0);
                cell.SetCellValue(rowIndex - 1);
                questionIndex = 2;
                names = [];
            }

            names.Add(_answers.Key);
            foreach (var answer in _answers.Value)
            {
                cell = row.CreateCell(questionIndex);
                if (answer.Count == 0)
                {
                    var cellStyle = workbook.CreateCellStyle();
                    cellStyle.FillPattern = FillPattern.SolidForeground;
                    cellStyle.FillForegroundColor = IndexedColors.Gold.Index;
                    cell.CellStyle = cellStyle;
                }
                else if (answer.Count == 1)
                {
                    cell.SetCellValue(answer[0]);
                }
                else if (answer.Count > 1)
                {
                    var cellStyle = workbook.CreateCellStyle();
                    cellStyle.FillPattern = FillPattern.SolidForeground;
                    cellStyle.FillForegroundColor = IndexedColors.Coral.Index;
                    cell.CellStyle = cellStyle;
                    cell.SetCellValue(string.Join(";", answer));
                }

                questionIndex++;
            }

            // last page
            if ((itemIndex + 1) % Pages.Count == 0 || itemIndex + 1 == Answers.Count)
            {
                cell = row.CreateCell(1);
                cell.SetCellValue(string.Join(";", names));
            }

            itemIndex++;
        }

        MemoryStream stream = new();
        workbook.Write(stream);
        return Convert.ToBase64String(stream.ToArray());
    }
}
