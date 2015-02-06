namespace OpenStackNetAnalyzers
{
    /// <summary>
    /// A wrapper for <see cref="HtmlPrinter"/> that keeps track if the last symbol has been a newline.
    /// </summary>
    internal sealed class DocumentationCommentTextWriter
    {
        private System.IO.TextWriter _inner;
        private char _last = '\n';
        private bool _windowsNewLine;
        private char[] _newline;

        /// <summary>
        /// A reusable char buffer. This is used internally in <see cref="Write(Syntax.StringPart)"/> (and thus will modify the buffer)
        /// but can also be used from <see cref="HtmlPrinter"/> class.
        /// </summary>
        internal char[] Buffer = new char[256];

        public DocumentationCommentTextWriter(System.IO.TextWriter inner)
        {
            _inner = inner;

            var nl = inner.NewLine;
            _newline = nl.ToCharArray();
            _windowsNewLine = nl == "\r\n";
        }

        public void WriteLine()
        {
            _inner.Write(_newline);
            _last = '\n';
        }

        public void Write(string value)
        {
            if (value.Length == 0)
                return;

            if (Buffer.Length < value.Length)
                Buffer = new char[value.Length];

            value.CopyTo(0, Buffer, 0, value.Length);

            if (_windowsNewLine)
            {
                var lastPos = 0;
                var pos = lastPos;
                var lastC = _last;

                while (-1 != (pos = value.IndexOf('\n', pos, value.Length - pos + 0)))
                {
                    lastC = pos == 0 ? _last : value[pos - 1];

                    if (lastC != '\r')
                    {
                        _inner.Write(Buffer, lastPos - 0, pos - lastPos);
                        _inner.Write('\r');
                        lastPos = pos;
                    }

                    pos++;
                }

                _inner.Write(Buffer, lastPos - 0, value.Length - lastPos + 0);
            }
            else
            {
                _inner.Write(Buffer, 0, value.Length);
            }

            _last = Buffer[value.Length - 1];
        }

        /// <summary>
        /// Writes a value that is known not to contain any newlines.
        /// </summary>
        public void WriteConstant(char[] value)
        {
            _last = 'c';
            _inner.Write(value, 0, value.Length);
        }

        /// <summary>
        /// Writes a value that is known not to contain any newlines.
        /// </summary>
        public void WriteConstant(char[] value, int startIndex, int length)
        {
            _last = 'c';
            _inner.Write(value, startIndex, length);
        }

        /// <summary>
        /// Writes a value that is known not to contain any newlines.
        /// </summary>
        public void WriteConstant(string value)
        {
            _last = 'c';
            _inner.Write(value);
        }

        /// <summary>
        /// Writes a value that is known not to contain any newlines.
        /// </summary>
        public void WriteLineConstant(string value)
        {
            _last = '\n';
            _inner.Write(value);
            _inner.Write(_newline);
        }

        public void Write(char[] value, int index, int count)
        {
            if (value == null || count == 0)
                return;

            if (_windowsNewLine)
            {
                var lastPos = index;
                var lastC = _last;
                int pos = index;

                while (pos < index + count)
                {
                    if (value[pos] != '\n')
                    {
                        pos++;
                        continue;
                    }

                    lastC = pos == index ? _last : value[pos - 1];

                    if (lastC != '\r')
                    {
                        _inner.Write(value, lastPos, pos - lastPos);
                        _inner.Write('\r');
                        lastPos = pos;
                    }

                    pos++;
                }

                _inner.Write(value, lastPos, index + count - lastPos);
            }
            else
            {
                _inner.Write(value, index, count);
            }

            _last = value[index + count - 1];
        }

        public void Write(char value)
        {
            if (_windowsNewLine && _last != '\r' && value == '\n')
                _inner.Write('\r');

            _last = value;
            _inner.Write(value);
        }

        /// <summary>
        /// Adds a newline if the writer does not currently end with a newline.
        /// </summary>
        public void EnsureLine()
        {
            if (_last != '\n')
                WriteLine();
        }
    }
}
