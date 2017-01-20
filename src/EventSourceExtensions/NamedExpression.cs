using System;

namespace EventSourceExtensions
{
    internal sealed class NamedExpression
    {
        public Delegate Func { get; }
        public string Name { get; }
        public int Index { get; }

        public NamedExpression(Delegate func, string name)
        {
            Func = func;
            Name = name;
        }

        private NamedExpression(Delegate func, string name, int index) : this(func, name)
        {
            Index = index;
        }

        public NamedExpression WithIndex(int index)
        {
            return new NamedExpression(Func, Name, index);
        }
    }
}