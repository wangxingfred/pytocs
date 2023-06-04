using Pytocs.Core.CodeModel;

namespace Pytocs.Core.Translate.Special;

public static class TableTranslator
{
    public static CodeExpression? Translate(CodeGenerator m, CodeFieldReferenceExpression method, CodeExpression[] args)
    {
        return method.FieldName switch
        {
            "concat" => TranslateConcat(m, args),
            "insert" => TranslateInsert(m, args),
            "remove" => TranslateRemove(m, args),
            "sort" => TranslateSort(m, args),
            "unpack" => TranslateUnpack(m, args),
            _ => null
        };
    }

    private static CodeExpression? TranslateConcat(CodeGenerator m, CodeExpression[] args)
    {
        if (args.Length == 1)
        {
            return m.ApplyMethod(args[0], "Concat");
        }

        var methodArgs = new CodeExpression[args.Length - 1];
        System.Array.Copy(args, 1, methodArgs, 0, methodArgs.Length);
        return m.ApplyMethod(args[0], "Concat", methodArgs);

        // var stringType = m.TypeRefExpr("string");
        // var methodRef = m.MethodRef(stringType, "Join");
        //
        // switch (args.Length)
        // {
        // case 1:
        //     // table.concat(list) -> string.Join(null, list)
        //     return m.Appl(methodRef, new CodePrimitiveExpression(null), args[0]);
        //
        // case 2: // table.concat(list, sep) -> string.Join(sep, list)
        //     return m.Appl(methodRef, args[1], args[0]);
        // case 4:
        // {
        //     // table.concat(list, seq, i, j) -> string.Join(sep, list.GetRange(i - 1, j - i + 1))
        //
        //     // i - 1
        //     var rangeStart =
        //         new CodeBinaryOperatorExpression(args[2], CodeOperatorType.Sub, new CodeNumericLiteral("1"));
        //
        //     // j - i + 1
        //     var rangeCount =
        //         new CodeBinaryOperatorExpression(
        //             new CodeBinaryOperatorExpression(args[3], CodeOperatorType.Sub, args[2]),
        //             CodeOperatorType.Add, new CodeNumericLiteral("1")
        //         );
        //
        //     // list.GetRange(i - 1, j - i + 1)
        //     var getRange = m.ApplyMethod(args[0], "GetRange", rangeStart, rangeCount);
        //
        //     return m.Appl(methodRef, args[1], getRange);
        // }
        // default:
        //     return null;
        // }
    }

    private static CodeExpression? TranslateInsert(CodeGenerator m, CodeExpression[] args)
    {
        switch (args.Length)
        {
        case 2:
            return m.ApplyMethod(args[0], "Add", args[1]);
        case 3:
        {
            return m.ApplyMethod(args[0], "Insert", args[1], args[2]);
        }
        default:
            return null;
        }
    }

    private static CodeExpression? TranslateRemove(CodeGenerator m, CodeExpression[] args)
    {
        switch (args.Length)
        {
        case 1:
        {
            var listCount = m.Access(args[0], "Count");
            return m.ApplyMethod(args[0], "RemoveAt", listCount);
        }
        case 2:
        {
            return m.ApplyMethod(args[0], "RemoveAt", args[1]);
        }
        default:
            return null;
        }
    }

    private static CodeExpression? TranslateSort(CodeGenerator m, CodeExpression[] args)
    {
        return args.Length switch
        {
            1 => m.ApplyMethod(args[0], "Sort"),
            2 => m.ApplyMethod(args[0], "Sort", args[1]),
            _ => null
        };
    }

    private static CodeExpression? TranslateUnpack(CodeGenerator m, CodeExpression[] args)
    {
        return args.Length == 1 ? m.ApplyMethod(args[0], "ToArray") : null;
    }
}