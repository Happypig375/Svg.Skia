﻿using Xml;

namespace Svg.FilterEffects
{
    [Element("feImage")]
    public class SvgImage : SvgFilterPrimitive, ISvgPresentationAttributes, ISvgStylableAttributes
    {
        public override void Print(string indent)
        {
            base.Print(indent);
            // TODO:
        }
    }
}
