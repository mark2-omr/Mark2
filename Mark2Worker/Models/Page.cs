namespace Mark2Worker;

using System.Collections.Generic;

public class Page
{
    public List<Question> Questions { get; set; }

    public Page()
    {
        Questions = [];
    }
}
