using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pytocs.Core.Syntax;

public class Reader
{
    private string _text;

    private int _index = 0;

    public Reader(TextReader rdr)
    {
        _text = rdr.ReadToEnd();
    }

    public int Peek()
    {
        if (_index >= _text.Length) return - 1;

        return _text[_index];
    }
    
    public int Read()
    {
        if (_index >= _text.Length) return - 1;

        return _text[_index++];
    }

    /// <summary>
    /// 将字符读入到StringBuilder中，直到遇到字符ch
    /// </summary>
    /// <param name="ch">停止字符，这个字符不读入StringBuilder中</param>
    /// <param name="sb"></param>
    /// <returns></returns>
    public int ReadUntil(char ch, StringBuilder sb)
    {
        var length = _text.Length;
        var startIndex = _index;
        var stopIndex = startIndex;
        
        while (stopIndex < length)
        {
            var c = _text[stopIndex];
            if (c == ch) break;

            ++stopIndex;
        }

        var count = stopIndex - startIndex;
        if (count > 0)
        {
            sb.Append(_text, startIndex, count);
            _index = stopIndex;
        }

        return count;
    }

    /// <summary>
    ///  跳过N个字符串
    /// </summary>
    /// <param name="count"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void Consume(int count)
    {
        var index = _index + count;

        if (count < 0 || index > _text.Length)
        {
            throw new ArgumentOutOfRangeException($"count={count}, _index={_index}, length={_text.Length}");
        }

        _index = index;
    }

    /// <summary>
    /// 从当前字符开始，检查是否匹配输入字符串
    /// </summary>
    /// <param name="str">要匹配的字符串</param>
    /// <returns></returns>
    public bool Match(string str)
    {
        var count = 0;
        var length = _text.Length;
        
        foreach (var c in str)
        {
            var index = _index + count;
            if (index >= length) return false;
            if (_text[index] != c) return false;

            ++count;
        }

        return true;
    }

    /// <summary>
    /// 从当前字符开始，检查是否匹配输入字符串，如果匹配则消耗/跳过这些字符，并返回true
    /// </summary>
    /// <param name="str">要匹配的字符串</param>
    /// <returns></returns>
    public bool MatchAndConsume(string str)
    {
        if (Match(str))
        {
            Consume(str.Length);
            return true;
        }

        return false;
    }
}