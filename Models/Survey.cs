﻿namespace Mark2;

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
    public string? title;
    public List<RepositoryPayload> repositoryPayloads = new();

    public IList<IBrowserFile> imageFiles;
    public double areaThreshold;
    public double colorThreshold;
    public List<Page> pages;

    public Dictionary<string, List<List<int>>> answers;
    public string selectedLogImage;

    public Survey()
    {
        imageFiles = new List<IBrowserFile>();
        pages = new();
        answers = new();
        selectedLogImage = string.Empty;
    }

    public async Task FetchRepository(string surveyId)
    {
        HttpClient client = new();
        string url = "https://mark2-repository.defrag.works/api/" + surveyId;
        var repository = await client.GetFromJsonAsync<Repository>(url);
        if (repository != null)
        {
            this.title = repository.name;
            this.repositoryPayloads = repository.payloads;
        }
    }

    public void SetupPositionsFromRepository(string repositoryPayloadName)
    {
        foreach (var payload in this.repositoryPayloads)
        {
            if (payload.name == repositoryPayloadName && payload.values != null)
            {
                pages = new();
                var row = payload.values[0];

                List<int> vs = new();
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

                for (int i = 3; i < payload.values.Count; i++)
                {
                    row = payload.values[i];
                    int pageNumber = row[2].GetInt32();
                    while (pages.Count() < pageNumber)
                    {
                        pages.Add(new Page());
                    }

                    Question question = new();
                    question.text = row[1].ToString();
                    question.type = row[3].GetInt32();

                    for (int j = 1; j <= row.Count / 4; j++)
                    {
                        try
                        {
                            Area area = new(row[j * 4].GetInt32(),
                                row[j * 4 + 1].GetInt32(),
                                row[j * 4 + 2].GetInt32(),
                                row[j * 4 + 3].GetInt32());
                            area.v = vs[j - 1];

                            question.areas.Add(area);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    pages[pageNumber - 1].questions.Add(question);
                }
            }
        }
    }

    public void SetupPositionsFromFile(MemoryStream ms)
    {
        pages = new();
        var workbook = new XSSFWorkbook(ms);
        var sheet = workbook.GetSheetAt(0);
        var row = sheet.GetRow(0);

        List<int> vs = new();
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
                while (pages.Count() < pageNumber)
                {
                    pages.Add(new Page());
                }

                question = new();
                question.text = row.GetCell(1).ToString();
                question.type = Convert.ToInt32(row.GetCell(3).NumericCellValue);
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
                    area.v = vs[j - 1];

                    question.areas.Add(area);
                }
                catch (Exception)
                {
                }
            }
            pages[pageNumber - 1].questions.Add(question);
        }
    }

    public async Task Recognize(int index, IJSRuntime js, bool updateLogImage = false)
    {
        MemoryStream stream = new();
        await imageFiles[index].OpenReadStream(1024 * 1024 * 24).CopyToAsync(stream);
        var image = Image.Load<Rgba32>(stream.ToArray());
        Item item = new(index, pages[index % pages.Count()], colorThreshold, areaThreshold,
                        imageFiles[index].Name, image, js);
        await item.Recognize();
        answers[item.name] = item.answers;
        if (updateLogImage)
        {
            selectedLogImage = item.LogImageBase64();
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
        foreach (var page in pages)
        {
            foreach (var question in page.questions)
            {
                cell = row.CreateCell(questionIndex + 2);
                cell.SetCellValue(questionIndex + 1);
                questionIndex++;
            }
        }

        // second header
        row = sheet.CreateRow(1);
        questionIndex = 0;
        foreach (var page in pages)
        {
            foreach (var question in page.questions)
            {
                cell = row.CreateCell(questionIndex + 2);
                cell.SetCellValue(question.text);
                questionIndex++;
            }
        }

        int rowIndex = 1;
        int itemIndex = 0;
        List<string> names = new();
        foreach (var _answers in answers.OrderBy(d => d.Key, StringComparison.OrdinalIgnoreCase.WithNaturalSort()))
        {
            // first page
            if (itemIndex % pages.Count == 0)
            {
                rowIndex++;
                row = sheet.CreateRow(rowIndex);
                cell = row.CreateCell(0);
                cell.SetCellValue(rowIndex - 1);
                questionIndex = 2;
                names = new();
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
            if ((itemIndex + 1) % pages.Count == 0 || itemIndex + 1 == answers.Count)
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
