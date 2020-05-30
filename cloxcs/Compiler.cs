#if DEBUG
#define DEBUG_PRINT_CODE // clox common.h
#endif

#define NAN_BOXING
#if NAN_BOXING
using Value_t = System.UInt64;
#endif

namespace cloxcs
{
    public struct Parser
    {
        public Token current;
        public Token previous;
        public bool hadError;
        public bool panicMode;
    }

    public enum Precedence
    {
        PREC_NONE,
        PREC_ASSIGNMENT,  // =        
        PREC_OR,          // or       
        PREC_AND,         // and      
        PREC_EQUALITY,    // == !=    
        PREC_COMPARISON,  // < > <= >=
        PREC_TERM,        // + -      
        PREC_FACTOR,      // * /      
        PREC_UNARY,       // ! -      
        PREC_CALL,        // . ()     
        PREC_PRIMARY
    }

    public delegate void ParseFn(bool canAssign);

    public struct ParseRule
    {
        public ParseFn prefix; // csharp delegate
        public ParseFn infix; // clox function pointer
        public Precedence precedence;

        public ParseRule(ParseFn prefix, ParseFn infix, Precedence precedence)
        { this.prefix = prefix; this.infix = infix; this.precedence = precedence; }
    }

    public struct Local
    {
        public Token name;
        public int depth;
        public bool isCaptured;
    }

    public struct Upvalue
    {
        public byte index;
        public bool isLocal;
    }

    public enum FunctionType
    {
        TYPE_FUNCTION,
        TYPE_INITIALIZER,
        TYPE_METHOD,
        TYPE_SCRIPT
    }

    public class Compiler_t
    {
        public Compiler_t enclosing; // Csharp struct doesn't allow recursive defintion, changed to class
        public ObjFunction function;
        public FunctionType type;

        public Local[] locals = new Local[Compiler.UINT8_COUNT];
        public int localCount;
        public Upvalue[] upvalues = new Upvalue[Compiler.UINT8_COUNT];
        public int scopeDepth;
    }

    public class ClassCompiler
    {
        public ClassCompiler enclosing;
        public Token name;
        public bool hasSuperclass;
    }

    static class Compiler
    {
        public const int UINT8_COUNT = (int)byte.MaxValue + 1;

        static Parser parser;
        static Compiler_t current = null;
        static ClassCompiler currentClass = null;

        private static Chunk_t currentChunk()
        {
            return current.function.chunk;
        }

        private static void errorAt(ref Token token, string message)
        {
            if (parser.panicMode)
                return;
            parser.panicMode = true;

            System.Console.Write("[line {0}] Error", token.line.ToString());

            if (token.type == TokenType.TOKEN_EOF)
            {
                System.Console.Write(" at end");
            }
            else if (token.type == TokenType.TOKEN_ERROR)
            {
                // Nothing.
            }
            else
            {
                System.Console.Write(" at '{0}'",  new string(token._char_ptr, token.start, token.length));
            }

            System.Console.WriteLine(": {0}", message);
            parser.hadError = true;
        }

        private static void error(string message)
        {
            errorAt(ref parser.previous, message);
        }

        private static void errorAtCurrent(string message)
        {
            errorAt(ref parser.current, message);
        }

        private static void advance()
        {
            parser.previous = parser.current;

            for (;;)
            {
                parser.current = Scanner.scanToken();
                if (parser.current.type != TokenType.TOKEN_ERROR)
                    break;

                errorAtCurrent(new string(parser.current._char_ptr, parser.current.start, parser.current.length));
            }
        }

        private static void consume(TokenType type, string message)
        {
            if (parser.current.type == type)
            {
                advance();
                return;
            }

            errorAtCurrent(message);
        }

        private static bool check(TokenType type)
        {
            return parser.current.type == type;
        }

        private static bool match(TokenType type)
        {
            if (!check(type))
                return false;
            advance();
            return true;
        }

        private static void emitByte(byte byte_)
        {
            Chunk_t _chunk = currentChunk();
            Chunk.writeChunk(ref _chunk, byte_, parser.previous.line);
            current.function.chunk = _chunk; // workaround
        }
        private static void emitByte(OpCode op) { emitByte((byte)op); }

        private static void emitBytes(byte byte1, byte byte2)
        {
            emitByte(byte1);
            emitByte(byte2);
        }
        private static void emitBytes(OpCode op1, OpCode op2) { emitBytes((byte)op1, (byte)op2); }


        private static void emitLoop(int loopStart)
        {
            emitByte(OpCode.OP_LOOP);

            int offset = currentChunk().count - loopStart + 2;
            if (offset > ushort.MaxValue)
                error("Loop body too large.");

            emitByte((byte)((offset >> 8) & 0xff));
            emitByte((byte)(offset & 0xff));
        }

        private static int emitJump(byte instruction)
        {
            emitByte(instruction);
            emitByte(0xff);
            emitByte(0xff);
            return currentChunk().count - 2;
        }
        private static int emitJump(OpCode instruction) { return emitJump((byte)instruction); }

        private static void emitReturn()
        {
            if (current.type == FunctionType.TYPE_INITIALIZER)
            {
                emitBytes((byte)OpCode.OP_GET_LOCAL, 0);
            }
            else
            {
                emitByte(OpCode.OP_NIL);
            }

            emitByte(OpCode.OP_RETURN);
        }

        private static byte makeConstant(Value_t value)
        {
            Chunk_t _chunk = currentChunk();
            int constant = Chunk.addConstant(ref _chunk, value);
            if (constant > byte.MaxValue)
            {
                error("Too many constant in one chunk.");
                return 0;
            }

            current.function.chunk = _chunk; // work around
            return (byte)constant;
        }

        private static void emitConstant(Value_t value)
        {
            emitBytes((byte)OpCode.OP_CONSTANT, makeConstant(value));
        }

        private static void patchJump(int offset)
        {
            int jump = currentChunk().count - offset - 2;

            if (jump > ushort.MaxValue)
            {
                error("Too much code to jump over.");
            }


            currentChunk().code[offset] = (byte)((jump >> 8) & 0xff);
            currentChunk().code[offset + 1] = (byte)(jump & 0xff);
        }

        private static void initCompiler(ref Compiler_t compiler, FunctionType type)
        {
            compiler.enclosing = current;
            compiler.function = null;
            compiler.type = type;
            compiler.localCount = 0;
            compiler.scopeDepth = 0;
            compiler.function = Object.newFunction();
            current = compiler;

            if (type != FunctionType.TYPE_SCRIPT)
            {
                current.function.name = Object.copyString(parser.previous._char_ptr, parser.previous.start, parser.previous.length);
            }

            Local local = current.locals[current.localCount++];
            local.depth = 0;
            local.isCaptured = false;

            if (type != FunctionType.TYPE_FUNCTION)
            {
                local.name._char_ptr = "this\0".ToCharArray();
                local.name.start = 0;
                local.name.length = 4;
            }
            else
            {
                local.name._char_ptr = new char[] { '\0' };
                local.name.start = 0;
                local.name.length = 0;
            }

            current.locals[current.localCount - 1] = local; // C sharp fix.
        }

        private static ObjFunction endCompiler()
        {
            emitReturn();
            ObjFunction function = current.function;

#if DEBUG_PRINT_CODE
            if (!parser.hadError)
            {
                Chunk_t _chunk = currentChunk();
                Debug.disassembleChunk(ref _chunk, function.name != null ? function.name.chars : "<script>".ToCharArray());
            }
#endif

            current = current.enclosing;
            return function;
        }

        private static void beginScope()
        {
            current.scopeDepth++;
        }

        private static void endScope()
        {
            current.scopeDepth--;

            while (current.localCount > 0 && current.locals[current.localCount - 1].depth > current.scopeDepth)
            {
                if (current.locals[current.localCount - 1].isCaptured)
                {
                    emitByte(OpCode.OP_CLOSE_UPVALUE);
                }
                else
                {
                    emitByte(OpCode.OP_POP);
                }
                current.localCount--;
            }
        }



        private static byte identifierConstant(ref Token name)
        {
            return makeConstant(Value.OBJ_VAL(Object.copyString(name._char_ptr, name.start, name.length)));
        }

        static bool identifiersEqual(ref Token a, ref Token b)
        {
            if (a.length != b.length)
                return false;

            return Cfun._memcmp(a._char_ptr, a.start, b._char_ptr, b.start, a.length);
        }

        private static int resolveLocal(ref Compiler_t compiler, ref Token name)
        {
            for (int i = compiler.localCount - 1; i >= 0; i--)
            {
                Local local = compiler.locals[i];
                if (identifiersEqual(ref name, ref local.name))
                {
                    if (local.depth == -1)
                    {
                        error("Cannot read local variable in its own intializer.");
                    }
                    return i;
                }
            }
            return -1;
        }

        private static int addUpvalue(ref Compiler_t compiler, byte index, bool isLocal)
        {
            int upvalueCount = compiler.function.upvalueCount;

            for (int i = 0; i < upvalueCount; i++)
            {
                Upvalue upvalue = compiler.upvalues[i];
                if (upvalue.index == index && upvalue.isLocal == isLocal)
                {
                    return i;
                }
            }

            if (upvalueCount == UINT8_COUNT)
            {
                error("Too many closure variables in function.");
                return 0;
            }

            compiler.upvalues[upvalueCount].isLocal = isLocal;
            compiler.upvalues[upvalueCount].index = index;
            return compiler.function.upvalueCount++;
        }

        private static int resolveUpvalue(ref Compiler_t compiler, ref Token name)
        {
            if (compiler.enclosing == null)
                return -1;

            int local = resolveLocal(ref compiler.enclosing, ref name);
            if (local != -1)
            {
                compiler.enclosing.locals[local].isCaptured = true;
                return addUpvalue(ref compiler, (byte)local, true);
            }

            int upvalue = resolveUpvalue(ref compiler.enclosing, ref name);
            if (upvalue != -1)
            {
                return addUpvalue(ref compiler, (byte)upvalue, false);
            }

            return -1;
        }

        private static void addLocal(ref Token name)
        {
            if (current.localCount == UINT8_COUNT)
            {
                error("Too many local variables in function.");
                return;
            }

            Local local = current.locals[current.localCount++];
            local.name = name;
            local.depth = -1;
            local.isCaptured = false;

            current.locals[current.localCount - 1] = local; // Csharp fix.
        }

        private static void declareVariable()
        {
            // Global variables are implicitly declared.
            if (current.scopeDepth == 0)
                return;

            Token name = parser.previous;

            for (int i = current.localCount - 1; i >= 0; i--)
            {
                Local local = current.locals[i];
                if (local.depth != -1 && local.depth < current.scopeDepth)
                {
                    break;
                }

                if (identifiersEqual(ref name, ref local.name))
                {
                    error("Variable with this name already declared in this scope.");
                }
            }

            addLocal(ref name);
        }

        private static byte parseVariable(string errorMessage)
        {
            consume(TokenType.TOKEN_IDENTIFIER, errorMessage);

            declareVariable();
            if (current.scopeDepth > 0)
                return 0;

            return identifierConstant(ref parser.previous);
        }

        private static void markInitialized()
        {
            if (current.scopeDepth == 0)
                return;
            current.locals[current.localCount - 1].depth = current.scopeDepth;
        }

        private static void defineVariable(byte global)
        {
            if (current.scopeDepth > 0)
            {
                markInitialized();
                return;
            }

            emitBytes((byte)OpCode.OP_DEFINE_GLOBAL, global);
        }

        private static byte argumentList()
        {
            byte argCount = 0;
            if (!check(TokenType.TOKEN_RIGHT_PAREN))
            {
                do
                {
                    expression();

                    if (argCount == 255)
                    {
                        error("Cannot have more than 255 arguments.");
                    }
                    argCount++;
                }
                while (match(TokenType.TOKEN_COMMA));
            }

            consume(TokenType.TOKEN_RIGHT_PAREN, "Expect ')' after arguments.");
            return argCount;
        }

        private static void and(bool canAsssign)
        {
            int endJump = emitJump((byte)OpCode.OP_JUMP_IF_FALSE);

            emitByte(OpCode.OP_POP);
            parsePrecedence(Precedence.PREC_AND);

            patchJump(endJump);
        }

        private static void binary(bool canAssign)
        {
            // Remember the operator.
            TokenType operatorType = parser.previous.type;

            // Compile the right operand.
            ParseRule rule = getRule(operatorType);
            parsePrecedence((Precedence)(rule.precedence + 1));

            // Emit the operator instruction.
            switch (operatorType)
            {
                case TokenType.TOKEN_BANG_EQUAL:
                    emitBytes(OpCode.OP_EQUAL, OpCode.OP_NOT);
                    break;
                case TokenType.TOKEN_EQUAL_EQUAL:
                    emitByte(OpCode.OP_EQUAL);
                    break;
                case TokenType.TOKEN_GREATER:
                    emitByte(OpCode.OP_GREATER);
                    break;
                case TokenType.TOKEN_GREATER_EQUAL:
                    emitBytes(OpCode.OP_LESS, OpCode.OP_NOT);
                    break;
                case TokenType.TOKEN_LESS:
                    emitByte(OpCode.OP_LESS);
                    break;
                case TokenType.TOKEN_LESS_EQUAL:
                    emitBytes(OpCode.OP_GREATER, OpCode.OP_NOT);
                    break;
                case TokenType.TOKEN_PLUS:
                    emitByte(OpCode.OP_ADD);
                    break;
                case TokenType.TOKEN_MINUS:
                    emitByte(OpCode.OP_SUBTRACT);
                    break;
                case TokenType.TOKEN_STAR:
                    emitByte(OpCode.OP_MULTIPLY);
                    break;
                case TokenType.TOKEN_SLASH:
                    emitByte(OpCode.OP_DIVIDE);
                    break;
                default:
                    return; // Unreachable.                              
            }

        }

        private static void call(bool canAssign)
        {
            byte argCount = argumentList();
            emitBytes((byte)OpCode.OP_CALL, argCount);
        }

        private static void dot(bool canAssign)
        {
            consume(TokenType.TOKEN_IDENTIFIER, "Expect property name after '.'.");
            byte name = identifierConstant(ref parser.previous);

            if (canAssign && match(TokenType.TOKEN_EQUAL))
            {
                expression();
                emitBytes((byte)OpCode.OP_SET_PROPERTY, name);
            }
            else if (match(TokenType.TOKEN_LEFT_PAREN))
            {
                byte argCount = argumentList();
                emitBytes((byte)OpCode.OP_INVOKE, name);
                emitByte(argCount);
            }
            else
            {
                emitBytes((byte)OpCode.OP_GET_PROPERTY, name);
            }
        }

        private static void literal(bool canAssign)
        {
            switch (parser.previous.type)
            {
                case TokenType.TOKEN_FALSE:
                    emitByte(OpCode.OP_FALSE);
                    break;
                case TokenType.TOKEN_NIL:
                    emitByte(OpCode.OP_NIL);
                    break;
                case TokenType.TOKEN_TRUE:
                    emitByte(OpCode.OP_TRUE);
                    break;
                default:
                    return; // Unreachable.
            }
        }

        private static void grouping(bool canAssign)
        {
            expression();
            consume(TokenType.TOKEN_RIGHT_PAREN, "Expect ')' after expression.");
        }

        private static void number(bool canAssign)
        {
            double value = Cfun._strtod(parser.previous._char_ptr, parser.previous.start, parser.previous.length);
            emitConstant(Value.NUMBER_VAL(value));
        }

        private static void or(bool canAssign)
        {
            int elseJump = emitJump(OpCode.OP_JUMP_IF_FALSE);
            int endJump = emitJump(OpCode.OP_JUMP);

            patchJump(elseJump);
            emitByte(OpCode.OP_POP);

            parsePrecedence(Precedence.PREC_OR);
            patchJump(endJump);
        }

        private static void string_(bool canAssign)
        {
            emitConstant(Value.OBJ_VAL(Object.copyString(parser.previous._char_ptr, parser.previous.start + 1, parser.previous.length - 2)));
        }

        private static void namedVariable(ref Token name, bool canAssign)
        {
            OpCode getOp, setOp;
            int arg = resolveLocal(ref current, ref name);
            if (arg != -1)
            {
                getOp = OpCode.OP_GET_LOCAL;
                setOp = OpCode.OP_SET_LOCAL;
            }
            else if ((arg = resolveUpvalue(ref current, ref name)) != -1)
            {
                getOp = OpCode.OP_GET_UPVALUE;
                setOp = OpCode.OP_SET_UPVALUE;
            }
            else
            {
                arg = identifierConstant(ref name);
                getOp = OpCode.OP_GET_GLOBAL;
                setOp = OpCode.OP_SET_GLOBAL;
            }


            if (canAssign && match(TokenType.TOKEN_EQUAL))
            {
                expression();
                emitBytes((byte)setOp, (byte)arg);
            }
            else
            {
                emitBytes((byte)getOp, (byte)arg);
            }
        }


        private static void variable(bool canAssign)
        {
            namedVariable(ref parser.previous, canAssign);
        }


        private static Token syntheticToken(string text)
        {
            Token token = new Token();
            token.start = 0;
            token.length = text.Length;

            token._char_ptr = text.ToCharArray();
            return token;
        }

        private static void super_(bool canAssign)
        {
            if (currentClass == null)
            {
                error("Cannot use 'super' outside of a class.");
            }
            else if (!currentClass.hasSuperclass)
            {
                error("Cannot use 'super' in a class with no superclass.");
            }

            consume(TokenType.TOKEN_DOT, "Expect '.' after super '.'");
            consume(TokenType.TOKEN_IDENTIFIER, "Expect superclass method name.");
            byte name = identifierConstant(ref parser.previous);

            Token _token = syntheticToken("this");
            namedVariable(ref _token, false);

            if (match(TokenType.TOKEN_LEFT_PAREN))
            {
                byte argCount = argumentList();
                _token = syntheticToken("super");
                namedVariable(ref _token, false);
                emitBytes((byte)OpCode.OP_SUPER_INVOKE, name);
                emitByte(argCount);
            }
            else
            {
                _token = syntheticToken("super");
                namedVariable(ref _token, false);
                emitBytes((byte)OpCode.OP_GET_SUPER, name);
            }

        }

        private static void this_(bool canAssign)
        {
            if (currentClass == null)
            {
                error("Cannot use 'this' outside of a class.");
                return;
            }
            variable(false);
        }

        private static void unary(bool canAssign)
        {
            TokenType operatorType = parser.previous.type;

            // Compile the operand
            parsePrecedence(Precedence.PREC_UNARY);

            // Emit the operator instruction.
            switch (operatorType)
            {
                case TokenType.TOKEN_BANG:
                    emitByte(OpCode.OP_NOT);
                    break;
                case TokenType.TOKEN_MINUS:
                    emitByte(OpCode.OP_NEGATE);
                    break;
                default:
                    return; // Unreachable.
            }
        }

        private static ParseRule[] rules = {//
      new ParseRule( grouping, call,    Precedence.PREC_CALL),       // TOKEN_LEFT_PAREN      
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_RIGHT_PAREN     
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_LEFT_BRACE
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_RIGHT_BRACE     
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_COMMA           
      new ParseRule(null,     dot,    Precedence.PREC_CALL ),       // TOKEN_DOT             
      new ParseRule(unary,    binary,  Precedence.PREC_TERM ),       // TOKEN_MINUS           
      new ParseRule(null,     binary,  Precedence.PREC_TERM ),       // TOKEN_PLUS            
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_SEMICOLON       
      new ParseRule(null,     binary,  Precedence.PREC_FACTOR ),     // TOKEN_SLASH           
      new ParseRule(null,     binary,  Precedence.PREC_FACTOR ),     // TOKEN_STAR            
      new ParseRule(unary,     null,    Precedence.PREC_NONE ),       // TOKEN_BANG            
      new ParseRule(null,     binary,    Precedence.PREC_EQUALITY ),       // TOKEN_BANG_EQUAL      
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_EQUAL           
      new ParseRule(null,     binary,    Precedence.PREC_EQUALITY ),       // TOKEN_EQUAL_EQUAL     
      new ParseRule(null,     binary,    Precedence.PREC_COMPARISON ),       // TOKEN_GREATER         
      new ParseRule(null,     binary,    Precedence.PREC_COMPARISON ),       // TOKEN_GREATER_EQUAL   
      new ParseRule(null,     binary,    Precedence.PREC_COMPARISON ),       // TOKEN_LESS            
      new ParseRule(null,     binary,    Precedence.PREC_COMPARISON ),       // TOKEN_LESS_EQUAL      
      new ParseRule(variable,     null,    Precedence.PREC_NONE ),       // TOKEN_IDENTIFIER      
      new ParseRule(string_,     null,    Precedence.PREC_NONE ),       // TOKEN_STRING          
      new ParseRule(number,   null,    Precedence.PREC_NONE ),       // TOKEN_NUMBER          
      new ParseRule(null,     and,    Precedence.PREC_AND ),       // TOKEN_AND             
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_CLASS           
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_ELSE            
      new ParseRule(literal,     null,    Precedence.PREC_NONE ),       // TOKEN_FALSE           
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_FOR             
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_FUN             
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_IF              
      new ParseRule(literal,     null,    Precedence.PREC_NONE ),       // TOKEN_NIL             
      new ParseRule(null,     or,    Precedence.PREC_OR ),       // TOKEN_OR              
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_PRINT           
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_RETURN          
      new ParseRule(super_,     null,    Precedence.PREC_NONE ),       // TOKEN_SUPER           
      new ParseRule(this_,     null,    Precedence.PREC_NONE ),       // TOKEN_THIS            
      new ParseRule(literal,     null,    Precedence.PREC_NONE ),       // TOKEN_TRUE            
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_VAR             
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_WHILE           
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_ERROR           
      new ParseRule(null,     null,    Precedence.PREC_NONE ),       // TOKEN_EOF             
    };

        private static void parsePrecedence(Precedence precedence)
        {
            advance();
            ParseFn prefixRule = getRule(parser.previous.type).prefix;
            if (prefixRule == null)
            {
                error("Expect expression.");
                return;
            }

            bool canAssign = precedence <= Precedence.PREC_ASSIGNMENT;
            prefixRule(canAssign);

            while (precedence <= getRule(parser.current.type).precedence)
            {
                advance();
                ParseFn infixRule = getRule(parser.previous.type).infix;
                infixRule(canAssign);
            }

            if (canAssign && match(TokenType.TOKEN_EQUAL))
            {
                error("Invalid assignment target.");
            }
        }

        static ParseRule getRule(TokenType type)
        {
            return rules[(int)type];
        }

        private static void expression()
        {
            parsePrecedence(Precedence.PREC_ASSIGNMENT);
        }

        private static void block()
        {
            while (!check(TokenType.TOKEN_RIGHT_BRACE) && !check(TokenType.TOKEN_EOF))
            {
                declaration();
            }

            consume(TokenType.TOKEN_RIGHT_BRACE, "Expect '}' after block.");
        }

        private static void function(FunctionType type)
        {
            Compiler_t compiler = new Compiler_t();
            initCompiler(ref compiler, type);
            beginScope();

            // Compile parameter list.
            consume(TokenType.TOKEN_LEFT_PAREN, "Expect '(' after function name.");
            if (!check(TokenType.TOKEN_RIGHT_PAREN))
            {
                do
                {
                    current.function.arity++;
                    if (current.function.arity > 255)
                    {
                        errorAtCurrent("Cannot have more than 255 parameters.");
                    }

                    byte paramConstant = parseVariable("Expect parameter name.");
                    defineVariable(paramConstant);
                }
                while (match(TokenType.TOKEN_COMMA));
            }
            consume(TokenType.TOKEN_RIGHT_PAREN, "Expect ')' after parameters.");

            // The body.
            consume(TokenType.TOKEN_LEFT_BRACE, "Expect '{' before funciton body.");
            block();

            // Create the function object.
            ObjFunction function = endCompiler();
            emitBytes((byte)OpCode.OP_CLOSURE, makeConstant(Value.OBJ_VAL(function)));

            for (int i = 0; i < function.upvalueCount; i++)
            {
                emitByte((byte)(compiler.upvalues[i].isLocal ? 1 : 0));
                emitByte(compiler.upvalues[i].index);

            }
        }

        private static void method()
        {
            consume(TokenType.TOKEN_IDENTIFIER, "Expect method name.");
            byte constant = identifierConstant(ref parser.previous);

            FunctionType type = FunctionType.TYPE_METHOD;

            if (parser.previous.length == 4 && Cfun._memcmp(parser.previous._char_ptr, parser.previous.start, "init", 4))
            {
                type = FunctionType.TYPE_INITIALIZER;
            }

            function(type);
            emitBytes((byte)OpCode.OP_METHOD, constant);
        }

        private static void classDeclaration()
        {
            consume(TokenType.TOKEN_IDENTIFIER, "Expect class name.");
            Token className = parser.previous;
            byte nameConstant = identifierConstant(ref parser.previous);
            declareVariable();

            emitBytes((byte)OpCode.OP_CLASS, nameConstant);
            defineVariable(nameConstant);

            ClassCompiler classCompiler = new ClassCompiler();
            classCompiler.name = parser.previous;
            classCompiler.hasSuperclass = false;
            classCompiler.enclosing = currentClass;
            currentClass = classCompiler; // & address of in clox

            if (match(TokenType.TOKEN_LESS))
            {
                consume(TokenType.TOKEN_IDENTIFIER, "Expect superclass name.");
                variable(false);

                if (identifiersEqual(ref className, ref parser.previous))
                {
                    error("A class cannot inherit from itself.");
                }

                beginScope();
                Token _local = syntheticToken("super");
                addLocal(ref _local);
                defineVariable(0);

                namedVariable(ref className, false);
                emitByte(OpCode.OP_INHERIT);
                classCompiler.hasSuperclass = true;
                currentClass = classCompiler; // CS ref fix
            }


            namedVariable(ref className, false);
            consume(TokenType.TOKEN_LEFT_BRACE, "Expect '{' before class body.");
            while (!check(TokenType.TOKEN_RIGHT_BRACE) && !check(TokenType.TOKEN_EOF))
            {
                method();
            }
            consume(TokenType.TOKEN_RIGHT_BRACE, "Expect '}' after class body.");
            emitByte(OpCode.OP_POP);

            if (classCompiler.hasSuperclass)
            {
                endScope();
            }

            currentClass = currentClass.enclosing;
        }

        private static void funDeclaration()
        {
            byte global = parseVariable("Expect function name.");
            markInitialized();

            function(FunctionType.TYPE_FUNCTION);
            defineVariable(global);
        }

        private static void varDeclaration()
        {
            byte global = parseVariable("Expect variable name.");

            if (match(TokenType.TOKEN_EQUAL))
            {
                expression();
            }
            else
            {
                emitByte(OpCode.OP_NIL);
            }
            consume(TokenType.TOKEN_SEMICOLON, "Expect ';' after variable declarion.");

            defineVariable(global);
        }

        private static void expressionStatement()
        {
            expression();
            consume(TokenType.TOKEN_SEMICOLON, "Expect ';' after expression.");
            emitByte(OpCode.OP_POP);
        }

        private static void forStatement()
        {
            beginScope();

            consume(TokenType.TOKEN_LEFT_PAREN, "Expect '(' after 'for'.");

            if (match(TokenType.TOKEN_SEMICOLON))
            {
                //No initializer.
            }
            else if (match(TokenType.TOKEN_VAR))
            {
                varDeclaration();
            }
            else
            {
                expressionStatement();
            }

            int loopStart = currentChunk().count;

            int exitJump = -1;
            if (!match(TokenType.TOKEN_SEMICOLON))
            {
                expression();
                consume(TokenType.TOKEN_SEMICOLON, "Expect ';' after loop condition.");

                //Jump out of loop if the condition is false.
                exitJump = emitJump(OpCode.OP_JUMP_IF_FALSE);
                emitByte(OpCode.OP_POP); //Condition.
            }

            if (!match(TokenType.TOKEN_RIGHT_PAREN))
            {
                int bodyJump = emitJump(OpCode.OP_JUMP);

                int incrementStart = currentChunk().count;
                expression();
                emitByte(OpCode.OP_POP);
                consume(TokenType.TOKEN_RIGHT_PAREN, "Expect ')' after for clauses.");

                emitLoop(loopStart);
                loopStart = incrementStart;
                patchJump(bodyJump);
            }

            statement();

            emitLoop(loopStart);

            if (exitJump != -1)
            {
                patchJump(exitJump);
                emitByte(OpCode.OP_POP); // Condition.
            }

            endScope();
        }

        static void ifStatement()
        {
            consume(TokenType.TOKEN_LEFT_PAREN, "Expect '(' after 'if' .");
            expression();
            consume(TokenType.TOKEN_RIGHT_PAREN, "Expect ')' after condition.");

            int thenJump = emitJump((byte)OpCode.OP_JUMP_IF_FALSE);
            emitByte(OpCode.OP_POP);
            statement();

            int elseJump = emitJump((byte)OpCode.OP_JUMP);

            patchJump(thenJump);
            emitByte(OpCode.OP_POP);

            if (match(TokenType.TOKEN_ELSE))
                statement();
            patchJump(elseJump);
        }

        private static void printStatement()
        {
            expression();
            consume(TokenType.TOKEN_SEMICOLON, "Expect ';' after value.");
            emitByte(OpCode.OP_PRINT);
        }

        private static void returnStatement()
        {
            if (current.type == FunctionType.TYPE_SCRIPT)
            {
                error("Cannot return from top-level code.");
            }

            if (match(TokenType.TOKEN_SEMICOLON))
            {
                emitReturn();
            }
            else
            {
                if (current.type == FunctionType.TYPE_INITIALIZER)
                {
                    error("Cannot return a value from an initializer.");
                }

                expression();
                consume(TokenType.TOKEN_SEMICOLON, "Expect ';' after return value.");
                emitByte(OpCode.OP_RETURN);
            }
        }

        private static void whileStatement()
        {
            int loopStart = currentChunk().count;

            consume(TokenType.TOKEN_LEFT_PAREN, "Expect '(' after 'while'.");
            expression();
            consume(TokenType.TOKEN_RIGHT_PAREN, "Expect ')' after condition.");

            int exitJump = emitJump(OpCode.OP_JUMP_IF_FALSE);

            emitByte(OpCode.OP_POP);
            statement();

            emitLoop(loopStart);

            patchJump(exitJump);
            emitByte(OpCode.OP_POP);
        }

        private static void synchronize()
        {
            parser.panicMode = false;

            while (parser.current.type != TokenType.TOKEN_EOF)
            {
                if (parser.previous.type == TokenType.TOKEN_SEMICOLON)
                    return;

                switch (parser.current.type)
                {
                    case TokenType.TOKEN_CLASS:
                    case TokenType.TOKEN_FUN:
                    case TokenType.TOKEN_VAR:
                    case TokenType.TOKEN_FOR:
                    case TokenType.TOKEN_IF:
                    case TokenType.TOKEN_WHILE:
                    case TokenType.TOKEN_PRINT:
                    case TokenType.TOKEN_RETURN:
                        return;
                    default:
                        break; // Do nothing.
                }

                advance();
            }
        }

        private static void declaration()
        {
            if (match(TokenType.TOKEN_CLASS))
            {
                classDeclaration();
            }
            else if (match(TokenType.TOKEN_FUN))
            {
                funDeclaration();
            }
            else if (match(TokenType.TOKEN_VAR))
            {
                varDeclaration();
            }
            else
            {
                statement();
            }

            if (parser.panicMode)
                synchronize();
        }

        private static void statement()
        {
            if (match(TokenType.TOKEN_PRINT))
            {
                printStatement();
            }
            else if (match(TokenType.TOKEN_FOR))
            {
                forStatement();
            }
            else if (match(TokenType.TOKEN_IF))
            {
                ifStatement();
            }
            else if (match(TokenType.TOKEN_RETURN))
            {
                returnStatement();
            }
            else if (match(TokenType.TOKEN_WHILE))
            {
                whileStatement();
            }
            else if (match(TokenType.TOKEN_LEFT_BRACE))
            {
                beginScope();
                block();
                endScope();
            }
            else
            {
                expressionStatement();
            }
        }

        public static ObjFunction compile(char[] source)
        {
            Scanner.initScanner(source);

            Compiler_t compiler = new Compiler_t();
            initCompiler(ref compiler, FunctionType.TYPE_SCRIPT);

            parser.hadError = false;
            parser.panicMode = false;

            advance();

            while (!match(TokenType.TOKEN_EOF))
            {
                declaration();
            }

            ObjFunction function = endCompiler();
            return parser.hadError ? null : function;
        }

        public static void markCompilerRoots()
        {
            Compiler_t compiler = current;
            while (compiler != null)
            {
                Memory.markObject((Obj)compiler.function);
                compiler = compiler.enclosing;
            }
        }
    }
}
