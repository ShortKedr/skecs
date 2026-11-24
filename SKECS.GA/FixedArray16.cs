using System.Runtime.InteropServices;

namespace SKECS.GA;

/// <summary>Inline storage for sixteen unmanaged values with bounds-checked ref access.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FixedArray16<T> where T : unmanaged
{
    public const int Length = 16;

    private T _element0;
    private T _element1;
    private T _element2;
    private T _element3;
    private T _element4;
    private T _element5;
    private T _element6;
    private T _element7;
    private T _element8;
    private T _element9;
    private T _element10;
    private T _element11;
    private T _element12;
    private T _element13;
    private T _element14;
    private T _element15;

    public static ref T At(ref FixedArray16<T> storage, int index)
    {
        switch (index)
        {
            case 0: return ref storage._element0;
            case 1: return ref storage._element1;
            case 2: return ref storage._element2;
            case 3: return ref storage._element3;
            case 4: return ref storage._element4;
            case 5: return ref storage._element5;
            case 6: return ref storage._element6;
            case 7: return ref storage._element7;
            case 8: return ref storage._element8;
            case 9: return ref storage._element9;
            case 10: return ref storage._element10;
            case 11: return ref storage._element11;
            case 12: return ref storage._element12;
            case 13: return ref storage._element13;
            case 14: return ref storage._element14;
            case 15: return ref storage._element15;
            default: throw new IndexOutOfRangeException();
        }
    }

    public static ref readonly T Read(in FixedArray16<T> storage, int index)
    {
        switch (index)
        {
            case 0: return ref storage._element0;
            case 1: return ref storage._element1;
            case 2: return ref storage._element2;
            case 3: return ref storage._element3;
            case 4: return ref storage._element4;
            case 5: return ref storage._element5;
            case 6: return ref storage._element6;
            case 7: return ref storage._element7;
            case 8: return ref storage._element8;
            case 9: return ref storage._element9;
            case 10: return ref storage._element10;
            case 11: return ref storage._element11;
            case 12: return ref storage._element12;
            case 13: return ref storage._element13;
            case 14: return ref storage._element14;
            case 15: return ref storage._element15;
            default: throw new IndexOutOfRangeException();
        }
    }
}
