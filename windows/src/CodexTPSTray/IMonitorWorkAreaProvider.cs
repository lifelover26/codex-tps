using System.Collections.Generic;
using System.Windows;

namespace CodexTPSTray;

public interface IMonitorWorkAreaProvider
{
    Rect GetPrimaryWorkArea();
    IReadOnlyList<Rect> GetAllWorkAreas();
}