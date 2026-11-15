namespace IrrModWorkbench.Models
{
    public record PropertyChange(
        string Selector,
        string PropertyName,
        string OldValue,
        string NewValue,
        string ValueType);
}