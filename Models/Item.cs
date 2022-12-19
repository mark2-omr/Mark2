namespace Mark2;

using Microsoft.JSInterop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public class Item
{
    public int pid;
    public string name;
    public Image<Rgba32> image;
    public Image<Rgba32> logImage;
    public List<Square> squares;
    public Page page;
    public double colorThreshold;
    public double areaThreshold;
    public List<List<int>> answers;
    private readonly IJSRuntime js;

    public Item(int pid, Page page, double colorThreshold, double areaThreshold, string name,
                Image<Rgba32> image, IJSRuntime js)
    {
        this.pid = pid;
        this.page = page;
        this.colorThreshold = colorThreshold;
        this.areaThreshold = areaThreshold;
        this.name = name;
        this.image = image;
        this.js = js;

        this.logImage = image.Clone();
        this.squares = DetectSquares();
        this.answers = new();
    }

    public List<Square> DetectSquares()
    {
        var squares = new List<Square>();
        var margin = new int[] { (int)(image.Width * 0.01), (int)(image.Height * 0.01) };
        var size = new int[] { (int)(image.Width * 0.3), (int)(image.Height * 0.08) };

        squares.Add(DetectSquare(margin, size));
        squares.Add(DetectSquare(new int[] { image.Width - margin[0] - size[0], margin[1] }, size));
        squares.Add(DetectSquare(new int[] { image.Width - margin[0] - size[0],
                image.Height - margin[1] - size[1] }, size));
        squares.Add(DetectSquare(new int[] { margin[0], image.Height - margin[1] - size[1] }, size));
        return squares;
    }

    Square DetectSquare(int[] topLeft, int[] size)
    {
        int[,] pixels = new int[size[0], size[1]];

        var index = 1;
        for (var i = 0; i < size[0]; i++)
        {
            for (var j = 0; j < size[1]; j++)
            {
                if (image[i + topLeft[0], j + topLeft[1]].R < 128)
                {
                    pixels[i, j] = index;
                    index++;
                }
                else
                {
                    pixels[i, j] = 0;
                }
            }
        }

        var previousPattern = pixels.ToString();
        while (true)
        {
            for (var i = 1; i < size[0] - 1; i++)
            {
                for (var j = 1; j < size[1] - 1; j++)
                {
                    for (var dx = -1; dx < 2; dx++)
                    {
                        for (var dy = -1; dy < 2; dy++)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }
                            else if (pixels[i, j] == 0 || pixels[i + dx, j + dy] == 0)
                            {
                                continue;
                            }
                            else if (pixels[i, j] < pixels[i + dx, j + dy])
                            {
                                pixels[i + dx, j + dy] = pixels[i, j];
                            }
                            else if (pixels[i, j] > pixels[i + dx, j + dy])
                            {
                                pixels[i, j] = pixels[i + dx, j + dy];
                            }
                        }
                    }
                }
            }

            if (previousPattern == pixels.ToString())
            {
                break;
            }
            else
            {
                previousPattern = pixels.ToString();
            }
        }

        Dictionary<int, int> frequency = new();
        for (var i = 0; i < size[0]; i++)
        {
            for (var j = 0; j < size[1]; j++)
            {
                var value = pixels[i, j];
                if (value > 0 && frequency.ContainsKey(value))
                {
                    frequency[value]++;
                }
                else if (value > 0)
                {
                    frequency[value] = 1;
                }
            }
        }
        var mostFrequent = frequency.OrderByDescending(v => v.Value).First().Key;

        List<int> xs = new();
        List<int> ys = new();
        for (var i = 0; i < size[0]; i++)
        {
            for (var j = 0; j < size[1]; j++)
            {
                if (pixels[i, j] == mostFrequent)
                {
                    xs.Add(i);
                    ys.Add(j);
                }
            }
        }

        var square = new Square(topLeft[0] + xs.Min(), topLeft[1] + ys.Min(),
            xs.Max() - xs.Min(), ys.Max() - ys.Min(),
            topLeft[0] + (int)xs.Average(), topLeft[1] + (int)ys.Average());

        FillRect(square.x, square.y, square.w, square.h, Rgba32.ParseHex("#FF0000FF"), 0.8f);

        return square;
    }

    public int[] BiLenearInterpoltation(int xp, int yp)
    {
        double w = 595.0;
        double h = 842.0;
        double xp1 = w * (0.14 + 0.015);
        double yp1 = h * (0.03 + 0.01);
        double xp2 = w * (0.83 + 0.015);
        double yp2 = h * (0.03 + 0.01);
        double xp3 = w * (0.83 + 0.015);
        double yp3 = h * (0.95 + 0.01);
        double xp4 = w * (0.14 + 0.015);
        double yp4 = h * (0.95 + 0.01);
        double u = 0.5;
        double v = 0.5;

        int maxIteration = 100;
        double er = 1.0e+6;
        double erMax = 1.0e-10;
        int iteration = 0;

        while (true)
        {
            var ex = xp1 - xp + (xp2 - xp1) * u + (xp4 - xp1) * v + (xp1 - xp2 + xp3 - xp4) * u * v;
            var ex2 = ex * ex * 0.5;
            var ey = yp1 - yp + (yp2 - yp1) * u + (yp4 - yp1) * v + (yp1 - yp2 + yp3 - yp4) * u * v;
            var ey2 = ey * ey * 0.5;

            var exu = ex * ((xp2 - xp1) + (xp1 - xp2 + xp3 - xp4) * v);
            var exv = ex * ((xp4 - xp1) + (xp1 - xp2 + xp3 - xp4) * u);
            var eyu = ey * ((yp2 - yp1) + (yp1 - yp2 + yp3 - yp4) * v);
            var eyv = ey * ((yp4 - yp1) + (yp1 - yp2 + yp3 - yp4) * u);

            var d = exu * eyv - exv * eyu;

            if (Math.Abs(d) < 1.0e-6)
            {
                u = new Random().NextDouble();
                v = new Random().NextDouble();
                continue;
            }

            var du = (eyv * ex2 - exv * ey2) / d;
            var dv = (-eyu * ex2 + exu * ey2) / d;

            u -= du;
            v -= dv;

            er = du * du + dv * dv;
            iteration++;

            if (!(er > erMax && iteration < maxIteration))
            {
                break;
            }
        }

        var xq = squares[0].cx + (squares[1].cx - squares[0].cx) * u + (squares[3].cx - squares[0].cx) * v
            + (squares[0].cx - squares[1].cx + squares[2].cx - squares[3].cx) * u * v;

        var yq = squares[0].cy + (squares[1].cy - squares[0].cy) * u + (squares[3].cy - squares[0].cy) * v
            + (squares[0].cy - squares[1].cy + squares[2].cy - squares[3].cy) * u * v;

        return new int[] { (int)xq, (int)yq };
    }

    public void FillRect(int x, int y, int w, int h, Rgba32 c, float a)
    {
        for (int i = x; i < w + x; i++)
        {
            for (int j = y; j < h + y; j++)
            {
                logImage[i, j] = new Rgba32(
                    ((float)logImage[i, j].R * (1.0f - a) + (float)c.R * a) / 255.0f,
                    ((float)logImage[i, j].G * (1.0f - a) + (float)c.G * a) / 255.0f,
                    ((float)logImage[i, j].B * (1.0f - a) + (float)c.B * a) / 255.0f);
            }
        }
    }

    public async Task Recognize()
    {
        answers = new();

        foreach (var (question, qid) in page.questions.Select((question, qid) => (question, qid)))
        {
            List<int> _answers = new();
            if (question.type == 1)
            {
                foreach (var area in question.areas)
                {
                    var topLeft = BiLenearInterpoltation(area.x, area.y);
                    var bottomRight = BiLenearInterpoltation(area.x + area.w, area.y + area.h);

                    int count = 0;
                    for (int i = topLeft[0]; i < bottomRight[0]; i++)
                    {
                        for (int j = topLeft[1]; j < bottomRight[1]; j++)
                        {
                            if (image[i, j].R < (int)((1 - colorThreshold) * 255))
                            {
                                count++;
                            }
                        }
                    }
                    if ((double)count / ((bottomRight[0] - topLeft[0]) * (bottomRight[1] - topLeft[1])) > areaThreshold)
                    {
                        _answers.Add(area.v);
                        FillRect(topLeft[0], topLeft[1], bottomRight[0] - topLeft[0], bottomRight[1] - topLeft[1],
                            Rgba32.ParseHex("#00FF00FF"), 0.4f);
                    }
                    else
                    {
                        FillRect(topLeft[0], topLeft[1], bottomRight[0] - topLeft[0], bottomRight[1] - topLeft[1],
                            Rgba32.ParseHex("#FF0000FF"), 0.4f);
                    }
                }
            }
            else if (question.type == 2)
            {
                var area = question.areas[0];
                var topLeft = BiLenearInterpoltation(area.x, area.y);
                var bottomRight = BiLenearInterpoltation(area.x + area.w, area.y + area.h);

                var cloneImage = image.Clone(img => img
                            .Crop(new Rectangle(topLeft[0], topLeft[1],
                                bottomRight[0] - topLeft[0], bottomRight[1] - topLeft[1]))
                            .Resize(28, 28));

                var data = new float[1 * 1 * 28 * 28];

                int i = 0;
                for (i = 0; i < data.Length; i++)
                {
                    data[i] = 0.0f;
                }
                i = 0;

                float average_x = 0.0f;
                float average_y = 0.0f;
                float average_count = 0.0f;

                for (int y = 2; y < 26; y++)
                {
                    for (int x = 2; x < 26; x++)
                    {
                        if (cloneImage[x, y].R < (int)((1 - colorThreshold) * 255))
                        {
                            average_x += (float)x;
                            average_y += (float)y;
                            average_count += 1.0f;
                        }
                    }
                }

                average_x = average_x / average_count;
                average_y = average_y / average_count;

                int dx = 14 - (int)average_x;
                int dy = 14 - (int)average_y;

                float color_scale = 1.0f;

                for (int y = 0; y < 28; y++)
                {
                    for (int x = 0; x < 28; x++)
                    {
                        int px = x + dx;
                        int py = y + dy;

                        if (px > 0 && py > 0 && px < 28 && py < 28)
                        {
                            i = 28 * py + px;
                            data[i] = (float)(255 - cloneImage[x, y].R) / 255.0f;
                        }
                    }
                }

                color_scale = 1.0f / data.Max();

                for (i = 0; i < data.Length; i++)
                {
                    data[i] = data[i] * color_scale;
                    if (data[i] > 1.0f)
                    {
                        data[i] = 1.0f;
                    }
                }

                try
                {
                    var ans = await js.InvokeAsync<int>("runOnnxRuntime", data);
                    _answers.Add(ans);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                FillRect(topLeft[0], topLeft[1], bottomRight[0] - topLeft[0], bottomRight[1] - topLeft[1],
                    Rgba32.ParseHex("#0000FFFF"), 0.4f);
            }
            else if (question.type == 3)
            {
            }

            answers.Add(_answers);
        }
    }

    public string LogImageBase64()
    {
        MemoryStream stream = new();
        logImage.SaveAsPng(stream);
        return Convert.ToBase64String(stream.ToArray());
    }
}
