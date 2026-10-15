namespace Mark2;

using System.Collections.Generic;

public class Question
{
    public string? Text { get; set; }
    public int Type { get; set; }
    public List<Area> Areas { get; set; }

    public Question()
    {
        Areas = [];
    }
}
