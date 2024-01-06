namespace Mark2;

using System.Collections.Generic;

public class Question
{
    public string? Text;
    public int Type;
    public List<Area> Areas;

    public Question()
    {
        Areas = new List<Area>();
    }
}
