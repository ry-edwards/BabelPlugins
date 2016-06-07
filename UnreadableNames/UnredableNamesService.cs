using System;
using System.Collections.Generic;
using System.Text;

using Babel;
using Babel.Xml;

namespace UnreadableNames
{
    internal class UnredableNamesService : IBabelRenamingService, IBabelRenamingListner
    {
        #region Fields
        private int _nameLength;
        private int _prefixLength;
        private IBabelRandomGeneratorService _random;
        private HashSet<string> _names;
        private string _seed;
        private int _maxNames; 
        #endregion

        #region Properties
        public int NameLength
        {
            get
            {
                return _nameLength;
            }
            set
            {
                if (value < 1 || value <= PrefixLength)
                    throw new ArgumentOutOfRangeException("NameLength");

                _nameLength = value;
            }
        }

        public int PrefixLength
        {
            get
            {
                return _prefixLength;
            }
            set
            {
                if (value < 0 || value >= NameLength)
                    throw new ArgumentOutOfRangeException("PrefixLength");

                _prefixLength = value;
            }
        }

        public string Alphabet { get; set; }

        public string Seed
        {
            get
            {
                return _seed;
            }
        }

        public int GeneratedNamesCount
        {
            get
            {
                return _names.Count;
            }
        } 
        #endregion

        #region Constructors
        public UnredableNamesService(IBabelRandomGeneratorService random)
        {
            if (random == null)
                throw new ArgumentNullException("Random generation service not available.");

            _random = random;
            _names = new HashSet<string>();

            // 32767 unique names
            _nameLength = 18;
            _prefixLength = 3;

            Alphabet = "abcdefghijklmopqrstuwxyz";
        } 
        #endregion

        #region IBabelRenamingService
        public string GetName(ISymbolDef symbol)
        {
            if (symbol.IsParameterDef)
            {
                var param = (ParameterDef)symbol;
                return Alphabet[param.Index % Alphabet.Length].ToString();
            }

            return NewName();
        }

        #endregion

        #region IBabelRenamingListner
        public void OnSymbolRenaming(ISymbolDef symbol, RenamingArguments args)
        {
            
            // Prevent renaming
            // args.Cancel = true;
        }

        public void OnSymbolRenamed(ISymbolDef symbol)
        {
        }

        #endregion

        #region Methods
        public void MakeSeed()
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < NameLength; i++)
            {
                int index = _random.NextInt() % Alphabet.Length;
                char ch = Alphabet[index];

                if (_random.NextDouble() > 0.5)
                    ch = SwitchCase(ch);

                builder.Append(ch);
            }

            _seed = builder.ToString();

            // Calculate max number of names it is possible to generate
            // with the given name length
            _maxNames = (int)Math.Pow(2, NameLength - PrefixLength) - 1;
        }

        private string NewName()
        {
            if (_names.Count >= _maxNames)
                throw new InvalidOperationException("Could not generate more names with the given seed length.");

            // Ensure unique name
            StringBuilder builder = new StringBuilder(_seed);
            string name = null;
            do
            {
                int pos = _random.NextInt(PrefixLength, NameLength);
                builder[pos] = SwitchCase(builder[pos]);
                name = builder.ToString();
            }
            while (_names.Contains(name));

            _names.Add(name);

            return name;
        }

        private static char SwitchCase(char ch)
        {
            if (Char.IsUpper(ch))
                return Char.ToLowerInvariant(ch);

            return Char.ToUpperInvariant(ch);
        }

        #endregion
    }
}