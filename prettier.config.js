// How the files the C# analyzers do not reach are formatted (#17).
//
// The C# sources are governed by the analyzers and the ruleset and are not
// prettier's business. What is left over is the configuration page, the
// workflows, the JSON and the documentation, and none of those had a formatter
// before this file existed.
//
// Almost everything here is prettier's default on purpose. A house style is a
// thing to argue about at every review; a default is a thing nobody has to hold
// an opinion on. Only the two settings below depart, and each says why.
module.exports = {
    // Line endings are not what this check governs, and pretending otherwise
    // would make it red for a reason that belongs to the clone rather than to
    // the file. The `.gitattributes` this repository carries declares one path
    // pattern, the fuzz seed corpus, and says nothing about text, so what lands
    // in a working copy is decided by that clone's `core.autocrlf`: a checkout on
    // Windows has CRLF and a checkout on Linux has LF, from identical bytes in
    // git. Prettier's default of `lf` would therefore refuse every file on one
    // of those two machines and pass on the other, which is a check that tells
    // you which operating system you are on.
    //
    // THIS PARAGRAPH SAID THE REPOSITORY CARRIED NO `.gitattributes` AT ALL. One
    // arrived with the seed corpus on #19, marking that directory `binary` so
    // git cannot normalise a seed's bytes. It changes nothing about the setting
    // and it changes what a reader has to check: the sentence to read is what
    // that file declares, not whether it exists.
    //
    // What this gives up is real and is not repaired elsewhere: a file committed
    // with carriage returns is accepted here. Adding `* text=auto eol=lf` to that
    // file is the change that would let this be `lf`, and it rewrites the working
    // copy of every tracked file in every clone, which is its own change and its
    // own argument.
    //
    // NOTHING REFUSES THE NEXT DRIFT OF THIS SHAPE. A comment here that describes
    // the tree is read by no check, this one was found by somebody grepping for
    // the file it names, and #281 is where that was written down.
    endOfLine: "auto",

    overrides: [
        {
            files: "*.md",
            options: {
                // Reflowing prose would rewrite every paragraph in this
                // repository's documentation the first time it ran, and would
                // then rewrite a whole paragraph whenever somebody changed a
                // word in the middle of it. The diff a reviewer reads is the
                // point of the documentation being in the tree at all, so line
                // breaks stay where they were written. This is prettier's
                // default and is stated rather than inherited, because it is
                // the one setting whose flipping would be invisible in the
                // configuration and enormous in the tree.
                proseWrap: "preserve",
            },
        },
    ],
};
