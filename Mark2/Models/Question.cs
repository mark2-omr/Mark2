namespace Mark2;

using System.Collections.Generic;
public class Question
{
    public string? text;
    public int type;
    public List<Area> areas;

    public Question()
    {
        areas = new List<Area>();
    }
}
