\ yet another Forth objects extension

\ written by Anton Ertl 1996, 1997
\ public domain; NO WARRANTY

\ This (in combination with compat/struct.fs) is in ANS Forth (with an
\ environmental dependence on case insensitivity; convert everything
\ to upper case for state sensitive systems).

\ compat/struct.fs and this file together use the following words:

\ from CORE :
\ : 1- + swap invert and ; DOES> @ immediate drop Create rot dup , >r
\ r> IF ELSE THEN over chars aligned cells 2* here - allot execute
\ POSTPONE ?dup 2dup move Variable 2@ 2! ! ['] >body = 2drop ' r@ +!
\ Constant recurse 1+ BEGIN 0= UNTIL negate Literal ." .
\ from CORE-EXT :
\ tuck pick nip true <> 0> erase Value :noname compile, 
\ from BLOCK-EXT :
\ \ 
\ from DOUBLE :
\ 2Constant 
\ from EXCEPTION :
\ throw catch 
\ from EXCEPTION-EXT :
\ abort" 
\ from FILE :
\ ( 
\ from FLOAT :
\ faligned floats 
\ from FLOAT-EXT :
\ dfaligned dfloats sfaligned sfloats 
\ from LOCAL :
\ TO 
\ from MEMORY :
\ allocate resize free 
\ from SEARCH :
\ get-order set-order wordlist get-current set-current 

\ needs struct.fs

\ helper words

: -rot ( a b c -- c a b )
    rot rot ;

: under+ ( a b c -- a+c b )
    rot + swap ;

: perform ( ... addr -- ... )
    @ execute ;

: ?dup-if ( compilation: -- orig ; run-time: n -- n|  )
    POSTPONE ?dup POSTPONE if ; immediate

: save-mem	( addr1 u -- addr2 u ) \ gforth
    \ copy a memory block into a newly allocated region in the heap
    swap >r
    dup allocate throw
    swap 2dup r> -rot move ;

: resize ( a-addr1 u -- a-addr2 ior ) \ gforth
    over
    if
	resize
    else
	nip allocate
    then ;

: extend-mem	( addr1 u1 u -- addr addr2 u2 )
    \ extend memory block allocated from the heap by u aus
    \ the (possibly reallocated) piece is addr2 u2, the extension is at addr
    over >r + dup >r resize throw
    r> over r> + -rot ;

\ data structures

struct
    cell% field object-map
end-struct object%

struct
    cell% 2* field interface-map
    cell%    field interface-map-offset \ aus
      \ difference between where interface-map points and where
      \ object-map points (0 for non-classes)
    cell%    field interface-offset \ aus
      \ offset of interface map-pointer in class-map (0 for classes)
end-struct interface%

interface%
    cell%    field class-parent
    cell%    field class-wordlist \ instance variables and other private words
    cell% 2* field class-inst-size \ size and alignment
end-struct class%

struct
    cell% field selector-offset \ the offset within the (interface) map
    cell% field selector-interface \ the interface offset
end-struct selector%

\ maps are not defined explicitly; they have the following structure:

\ pointers to interface maps (for classes) <- interface-map points here
\ interface%/class% pointer                <- (object-)map  points here
\ xts of methods 


\ code

\ selectors and methods

variable current-interface

: no-method ( -- )
    true abort" no method defined for this object/selector combination" ;

: do-class-method ( -- )
does> ( ... object -- ... )
    ( object )
    selector-offset @ over object-map @ + ( object xtp ) perform ;

: do-interface-method ( -- )
does> ( ... object -- ... )
    ( object selector-body )
    2dup selector-interface @ ( object selector-body object interface-offset )
    swap object-map @ + @ ( object selector-body map )
    swap selector-offset @ + perform ;

: method ( xt "name" -- )
    \ define selector with method xt
    create
    current-interface @ interface-map 2@ ( xt map-addr map-size )
    dup current-interface @ interface-map-offset @ - ,
    1 cells extend-mem current-interface @ interface-map 2! ! ( )
    current-interface @ interface-offset @ dup ,
    ( 0<> ) if
	do-interface-method
    else
	do-class-method
    then ;

: selector ( "name" -- )
    \ define a method selector for later overriding in subclasses
    ['] no-method method ;

: interface-override! ( xt sel-xt interface-map -- )
    \ xt is the new method for the selector sel-xt in interface-map
    swap >body ( xt map selector-body )
    selector-offset @ + ! ;

: class->map ( class -- map )
    \ compute the (object-)map for the class
    dup interface-map 2@ drop swap interface-map-offset @ + ;

: unique-interface-map ( class-map offset -- )
    \ if the interface at offset in class map is the same as its parent,
    \ copy it to make it unique; used for implementing a copy-on-write policy
    over @ class-parent @ class->map ( class-map offset parent-map )
    over + @ >r  \ the map for the interface for the parent
    + dup @ ( mapp map )
    dup r> =
    if
	@ interface-map 2@ save-mem drop
	swap !
    else
	2drop
    then ;

: class-override! ( xt sel-xt class-map -- )
    \ xt is the new method for the selector sel-xt in class-map
    over >body ( xt sel-xt class-map selector-body )
    selector-interface @ ( xt sel-xt class-map offset )
    ?dup-if \ the selector is for an interface
	2dup unique-interface-map
	+ @
    then
    interface-override! ;

: overrides ( xt "selector" -- )
    \ replace default method "method" in the current class with xt
    \ must not be used during an interface definition
    ' current-interface @ class->map class-override! ;

\ interfaces

\ every interface gets a different offset; the latest one is stored here
variable last-interface-offset 0 last-interface-offset !

: interface ( -- )
    interface% %allot >r
    0 0 r@ interface-map 2!
    -1 cells last-interface-offset +!
    last-interface-offset @ r@ interface-offset !
    0 r@ interface-map-offset !
    r> current-interface ! ;

: end-interface-noname ( -- interface )
    current-interface @ ;

: end-interface ( "name" -- )
    \ name execution: ( -- interface )
    end-interface-noname constant ;

\ classes

: add-class-order ( n1 class -- wid1 ... widn n+n1 )
    dup >r class-parent @
    ?dup-if
	recurse \ first add the search order for the parent class
    then
    r> class-wordlist @ swap 1+ ;

: push-order ( class -- )
    \ add the class's wordlist to the search-order (in front)
    >r get-order r> add-class-order set-order ;

: class ( parent-class -- align size )
    class% %allot >r
    dup interface-map 2@ save-mem r@ interface-map 2!
    dup interface-map-offset @ r@ interface-map-offset !
    r@ dup class->map !
    0 r@ interface-offset !
    dup r@ class-parent !
    wordlist r@ class-wordlist !
    r@ current-interface !
    r> push-order
    class-inst-size 2@ ;

: remove-class-order ( wid1 ... widn n+n1 class -- n1 )
    \ note: no checks, whether the wordlists are correct
    begin
	>r nip 1-
	r> class-parent @ dup 0=
    until
    drop ;

: drop-order ( class -- )
    \ note: no checks, whether the wordlists are correct
    >r get-order r> remove-class-order set-order ;

: end-class-noname ( align size -- class )
    current-interface @ dup drop-order class-inst-size 2!
    end-interface-noname ;

: end-class ( align size "name" -- )
    \ name execution: ( -- class )
    end-class-noname constant ;

\ visibility control

variable public-wordlist

: protected ( -- )
    current-interface @ class-wordlist @
    dup get-current <>
    if \ we are not protected already
	get-current public-wordlist !
    then
    set-current ;

: public ( -- )
    public-wordlist @ set-current ;

\ classes that implement interfaces

: front-extend-mem ( addr1 u1 u -- addr addr2 u2 )
    \ extend memory block allocated from the heap by u aus, with the
    \ old stuff coming at the end
    2dup + dup >r allocate throw ( addr1 u1 u addr2 ; R: u2 )
    dup >r + >r over r> rot move ( addr1 ; R: u2 addr2 )
    free throw
    r> dup r> ;
    
: implementation ( interface -- )
    dup interface-offset @ ( interface offset )
    current-interface @ interface-map-offset @ negate over - dup 0>
    if \ the interface does not fit in the present class-map
	>r current-interface @ interface-map 2@
	r@ front-extend-mem
	current-interface @ interface-map 2!
	r@ erase
	dup negate current-interface @ interface-map-offset !
	r>
    then ( interface offset n )
    drop >r
    interface-map 2@ save-mem drop ( map )
    current-interface @ dup interface-map 2@ drop
    swap interface-map-offset @ + r> + ! ;

\ this/self, instance variables etc.

\ rename "this" into "self" if you are a Smalltalk fiend
0 value this ( -- object )
: to-this ( object -- )
    TO this ;

\ another implementation, if you don't have (fast) values
\ variable thisp
\ : this ( -- object )
\     thisp @ ;
\ : to-this ( object -- )
\     thisp ! ;

: m: ( -- xt colon-sys ) ( run-time: object -- )
    :noname 
    POSTPONE this
    POSTPONE >r
    POSTPONE to-this ;

: ;m ( colon-sys -- ) ( run-time: -- )
    POSTPONE r>
    POSTPONE to-this
    POSTPONE ; ; immediate

: catch ( ... xt -- ... n )
    \ make it safe to call CATCH within a method.
    \ should also be done with all words containing CATCH.
    this >r catch r> to-this ;

\ the following is a bit roundabout; this is caused by the standard
\ disallowing to change the compilation wordlist between CREATE and
\ DOES> (see RFI 3)

: inst-something ( align1 size1 align size xt "name" -- align2 size2 )
    \ xt ( -- ) typically is for a DOES>-word
    get-current >r
    current-interface @ class-wordlist @ set-current
    >r create-field r> execute
    r> set-current ;

: do-inst-var ( -- )
does> \ name execution: ( -- addr )
    ( addr1 ) @ this + ;

: inst-var ( align1 offset1 align size "name" -- align2 offset2 )
    \ name execution: ( -- addr )
    ['] do-inst-var inst-something ;

: do-inst-value ( -- )
does> \ name execution: ( -- w )
    ( addr1 ) @ this + @ ;

: inst-value ( align1 offset1 "name" -- align2 offset2 )
    \ name execution: ( -- w )
    \ a cell-sized value-flavoured instance field
    cell% ['] do-inst-value inst-something ;

: <to-inst> ( w xt -- )
    >body @ this + ! ;

: [to-inst] ( compile-time: "name" -- ; run-time: w -- )
    ' >body @ POSTPONE literal
    POSTPONE this
    POSTPONE +
    POSTPONE ! ; immediate

\ class binding stuff

: <bind> ( class selector-xt -- xt )
    >body swap class->map over selector-interface @
    ?dup-if
	+ @
    then
    swap selector-offset @ + @ ;

: bind' ( "class" "selector" -- xt )
    ' execute ' <bind> ;

: bind ( ... object "class" "selector" -- ... )
    bind' execute ;

: [bind] ( compile-time: "class" "selector" -- ; run-time: ... object -- ... )
    bind' compile, ; immediate

: current' ( "selector" -- xt )
    current-interface @ ' <bind> ;

: [current] ( compile-time: "selector" -- ; run-time: ... object -- ... )
    current' compile, ; immediate

: [parent] ( compile-time: "selector" -- ; run-time: ... object -- ... )
    \ same as `[bind] "parent" "selector"', where "parent" is the
    \ parent class of the current class
    current-interface @ class-parent @ ' <bind> compile, ; immediate

\ the object class

\ because OBJECT has no parent class, we have to build it by hand
\ (instead of with class)

class% %allot current-interface !
current-interface 1 cells save-mem current-interface @ interface-map 2!
0 current-interface @ interface-map-offset !
0 current-interface @ interface-offset !
0 current-interface @ class-parent !
wordlist current-interface @ class-wordlist !
object%
current-interface @ push-order

:noname ( object -- )
    drop ;
method construct ( ... object -- )

:noname ( object -- )
    ." object:" dup . ." class:" object-map @ @ . ;
method print

end-class object

\ constructing objects

: init-object ( ... class object -- )
    swap class->map over object-map ! ( ... object )
    construct ;

: xt-new ( ... class xt -- object )
    \ makes a new object, using XT ( align size -- addr ) to allocate memory
    over class-inst-size 2@ rot execute
    dup >r init-object r> ;

: dict-new ( ... class -- object )
    \ makes a new object HERE in dictionary
    ['] %allot xt-new ;

: heap-new ( ... class -- object )
    \ makes a new object in ALLOCATEd memory
    ['] %alloc xt-new ;
