namespace cloxcs
{

    public enum TokenType
    {
        // Single-character tokens.                         
        TOKEN_LEFT_PAREN, TOKEN_RIGHT_PAREN,
        TOKEN_LEFT_BRACE, TOKEN_RIGHT_BRACE,
        TOKEN_COMMA, TOKEN_DOT, TOKEN_MINUS, TOKEN_PLUS,
        TOKEN_SEMICOLON, TOKEN_SLASH, TOKEN_STAR,

        // One or two character tokens.                     
        TOKEN_BANG, TOKEN_BANG_EQUAL,
        TOKEN_EQUAL, TOKEN_EQUAL_EQUAL,
        TOKEN_GREATER, TOKEN_GREATER_EQUAL,
        TOKEN_LESS, TOKEN_LESS_EQUAL,

        // Literals.                                        
        TOKEN_IDENTIFIER, TOKEN_STRING, TOKEN_NUMBER,

        // Keywords.                                        
        TOKEN_AND, TOKEN_CLASS, TOKEN_ELSE, TOKEN_FALSE,
        TOKEN_FOR, TOKEN_FUN, TOKEN_IF, TOKEN_NIL, TOKEN_OR,
        TOKEN_PRINT, TOKEN_RETURN, TOKEN_SUPER, TOKEN_THIS,
        TOKEN_TRUE, TOKEN_VAR, TOKEN_WHILE,

        TOKEN_ERROR,
        TOKEN_EOF
    }

    public struct Token
    {
        public TokenType type;
        public int start;
        public int length;
        public int line;
        public char[] _char_ptr;
    }


    public struct Scanner_t
    {
        public int start;
        public int current;
        public int line;
        public char[] _char_ptr;
    }


    class Scanner
    {
        static Scanner_t scanner;

        public static void initScanner(char[] source)
        {
            scanner.start = 0;
            scanner.current = 0;
            scanner.line = 1;
            scanner._char_ptr = source;
        }

        private static bool isAlpha(char c)
        {
            return (c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                c == '_';
        }

        private static bool isDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool isAtEnd()
        {
            return scanner._char_ptr[scanner.current] == '\0';
        }

        private static char advance()
        {
            scanner.current++;
            return scanner._char_ptr[scanner.current - 1];
        }

        private static char peek()
        {
            return scanner._char_ptr[scanner.current];
        }

        private static char peekNext()
        {
            if (isAtEnd())
                return '\0';
            return scanner._char_ptr[scanner.current + 1];
        }

        private static bool match(char expected)
        {
            if (isAtEnd())
                return false;
            if (scanner._char_ptr[scanner.current] != expected)
                return false;

            scanner.current++;
            return true;
        }

        private static Token makeToken(TokenType type)
        {
            Token token;
            token.type = type;
            token.start = scanner.start;
            token.length = scanner.current - scanner.start;
            token.line = scanner.line;

            token._char_ptr = scanner._char_ptr; // work around for pointers

            return token;
        }

        private static Token errorToken(string message)
        {
            Token token;
            token.type = TokenType.TOKEN_ERROR;
            token.start = 0;

            token.length = message.Length;
            token.line = scanner.line;

            token._char_ptr = message.ToCharArray(); // char[] string literals are a hassle in Chsarp

            return token;
        }

        private static void skipWhitespace()
        {
            for (;;)
            {
                char c = peek();
                switch (c)
                {
                    case ' ':
                    case '\r':
                    case '\t':
                        advance();
                        break;
                    case '\n':
                        scanner.line++;
                        advance();
                        break;

                    case '/':
                        if (peekNext() == '/')
                        {
                            // A comment goes until the end of the line.
                            while (peek() != '\n' && !isAtEnd())
                                advance();
                        }
                        else
                        {
                            return;
                        }
                        break;

                    default:
                        return;
                }
            }
        }



        private static TokenType checkKeyword(int start, int length, string rest, TokenType type)
        {
            if (scanner.current - scanner.start == start + length &&
               Cfun._memcmp(scanner._char_ptr, scanner.start + start, rest, length)) 
            {
                return type;
            }

            return TokenType.TOKEN_IDENTIFIER;
        }

        private static TokenType identifierType()
        {
            switch (scanner._char_ptr[scanner.start])
            {
                case 'a':
                    return checkKeyword(1, 2, "nd", TokenType.TOKEN_AND);
                case 'c':
                    return checkKeyword(1, 4, "lass", TokenType.TOKEN_CLASS);
                case 'e':
                    return checkKeyword(1, 3, "lse", TokenType.TOKEN_ELSE);
                case 'f':
                    if (scanner.current - scanner.start > 1)
                    {
                        switch (scanner._char_ptr[scanner.start + 1])
                        {
                            case 'a':
                                return checkKeyword(2, 3, "lse", TokenType.TOKEN_FALSE);
                            case 'o':
                                return checkKeyword(2, 1, "r", TokenType.TOKEN_FOR);
                            case 'u':
                                return checkKeyword(2, 1, "n", TokenType.TOKEN_FUN);
                        }
                    }
                    break;
                case 'i':
                    return checkKeyword(1, 1, "f", TokenType.TOKEN_IF);
                case 'n':
                    return checkKeyword(1, 2, "il", TokenType.TOKEN_NIL);
                case 'o':
                    return checkKeyword(1, 1, "r", TokenType.TOKEN_OR);
                case 'p':
                    return checkKeyword(1, 4, "rint", TokenType.TOKEN_PRINT);
                case 'r':
                    return checkKeyword(1, 5, "eturn", TokenType.TOKEN_RETURN);
                case 's':
                    return checkKeyword(1, 4, "uper", TokenType.TOKEN_SUPER);
                case 't':
                    if (scanner.current - scanner.start > 1)
                    {
                        switch (scanner._char_ptr[scanner.start + 1])
                        {
                            case 'h':
                                return checkKeyword(2, 2, "is", TokenType.TOKEN_THIS);
                            case 'r':
                                return checkKeyword(2, 2, "ue", TokenType.TOKEN_TRUE);
                        }
                    }
                    break;
                case 'v':
                    return checkKeyword(1, 2, "ar", TokenType.TOKEN_VAR);
                case 'w':
                    return checkKeyword(1, 4, "hile", TokenType.TOKEN_WHILE);
            }
            return TokenType.TOKEN_IDENTIFIER;
        }

        private static Token identifier()
        {
            while (isAlpha(peek()) || isDigit(peek()))
                advance();

            return makeToken(identifierType());
        }

        private static Token number()
        {
            while (isDigit(peek()))
                advance();

            // Look for a fractional part.
            if (peek() == '.' && isDigit(peekNext()))
            {
                // Consume the "."
                advance();
                while (isDigit(peek()))
                    advance();
            }

            return makeToken(TokenType.TOKEN_NUMBER);
        }

        private static Token string_()
        {
            while (peek() != '"' && !isAtEnd())
            {
                if (peek() == '\n')
                    scanner.line++;
                advance();
            }

            if (isAtEnd())
                return errorToken("Unterminated string.");

            // The closing quote.
            advance();

            return makeToken(TokenType.TOKEN_STRING);
        }

        public static Token scanToken()
        {
            skipWhitespace();

            scanner.start = scanner.current;
            if (isAtEnd())
                return makeToken(TokenType.TOKEN_EOF);

            char c = advance();
            if (isAlpha(c))
                return identifier();
            if (isDigit(c))
                return number();


            switch (c)
            {
                case '(':
                    return makeToken(TokenType.TOKEN_LEFT_PAREN);
                case ')':
                    return makeToken(TokenType.TOKEN_RIGHT_PAREN);
                case '{':
                    return makeToken(TokenType.TOKEN_LEFT_BRACE);
                case '}':
                    return makeToken(TokenType.TOKEN_RIGHT_BRACE);
                case ';':
                    return makeToken(TokenType.TOKEN_SEMICOLON);
                case ',':
                    return makeToken(TokenType.TOKEN_COMMA);
                case '.':
                    return makeToken(TokenType.TOKEN_DOT);
                case '-':
                    return makeToken(TokenType.TOKEN_MINUS);
                case '+':
                    return makeToken(TokenType.TOKEN_PLUS);
                case '/':
                    return makeToken(TokenType.TOKEN_SLASH);
                case '*':
                    return makeToken(TokenType.TOKEN_STAR);
                case '!':
                    return makeToken(match('=') ? TokenType.TOKEN_BANG_EQUAL : TokenType.TOKEN_BANG);
                case '=':
                    return makeToken(match('=') ? TokenType.TOKEN_EQUAL_EQUAL : TokenType.TOKEN_EQUAL);
                case '<':
                    return makeToken(match('=') ? TokenType.TOKEN_LESS_EQUAL : TokenType.TOKEN_LESS);
                case '>':
                    return makeToken(match('=') ? TokenType.TOKEN_GREATER_EQUAL : TokenType.TOKEN_GREATER);
                case '"':
                    return string_();
            }

            return errorToken("Unexpected character.");
        }
    }
}
