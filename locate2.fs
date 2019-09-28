require unix/libc.fs

#24 #80 2Constant plain-form

' (type) ' (emit) ' (cr) ' plain-form output: plain-out
: plain-output ( xt -- )
    op-vector @ >r  plain-out  catch  r> op-vector !  throw ;

: whereg ( "name" -- ) \ gforth
    \g Like @code{where}, but puts the output in the editor.  In
    \g Emacs, you can then use the compilation-mode commands
    \g (@pxref{Compilation Mode,,,emacs,GNU Emacs Manual}) to inspect
    \g specific occurences more closely.
    "/tmp/gforth-whereg-XXXXXX\0" save-mem 1- over mkstemp dup ?ior fd>file
    [:  "-*- mode: compilation; default-directory: \"" type
	here unused getcwd dup 0= ?errno-throw dup strlen type
	"\" -*-" type
	['] where plain-output
    ;] over outfile-execute close-file throw
    `edit-file-cmd >string-execute 2dup system drop free throw ;