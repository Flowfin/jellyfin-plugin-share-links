# The rule the test suite runs under

## The rule

A test in this repository runs:

- with no display
- with no administrator or root rights
- writing nowhere outside a temporary directory the test itself owns
- without reading or depending on the machine's certificate stores
- with no network
- without invoking an external binary

All six, every test, on every machine. The rule is stated once because the first
test somebody adds after forgetting it is the one that breaks the build for
everybody else, and it breaks it on somebody else's machine rather than on the
machine it was written on.

Nothing here is a preference about style. Each clause is a way a suite stops
being a suite: a test that needs a display cannot run in the container the gate
runs in; a test that needs root cannot run at all on a machine somebody uses;
a test that writes outside its own directory leaves the next run a different
run; a test that reads the machine's certificate stores passes on the machine
that has the certificate and fails everywhere else; a test that reaches the
network fails when the network does, which is a red build about somebody else's
outage; and a test that shells out is testing whatever that binary is today.

## What proves the suite obeys it

`.github/workflows/headless.yml` runs the suite in a container with no network
interface at all, as an unprivileged user, with every Linux capability dropped.
Not a container that merely does not use the network: `--network none` gives the
container a loopback interface and nothing else, so a test that reaches out gets
an error rather than a slow success.

The job does not take the container's word for any of that. Before it runs the
suite it proves the three conditions are real, because a container that quietly
had a network would let a suite that quietly used one pass:

- a TCP connection out of the container must fail
- `id -u` inside the container must not be 0
- a write to a root-owned path must be refused

If any of those three succeeds, the job reds without running the suite at all,
because a proving run under conditions that were not in force proves nothing.

**Once per server line, in the image that carries that line's runtime.** The tree
compiles `net9.0` against 10.11 and `net10.0` against 12.0, so "the suite" is two
legs, and an image carries the runtime of one line and not the other. The job runs
each leg in its own pinned image, proves the three conditions in both of them
rather than in one, and counts the tests each leg reported separately, so a leg
whose testhost never started reds by name instead of leaving a green check over a
run that did not happen.

Until #73 that was one leg. What ran was the packaged line, and the other was
compiled and tested on an ordinary runner but not proved to run offline or
unprivileged. The two legs run the same test code, which is the reason a reader
might think one sufficed; it is not one, because what this job is about is the
runtime the tests execute under rather than the code they execute.

## The two clauses no run can prove

The display clause and the certificate-store clause are held by the writing and
not by the run. A headless Linux container has no display and no user
certificate store to begin with, so a test that depended on either would fail
there for the same reason it would fail under this rule, and the run cannot tell
those two reasons apart. Nothing here refuses a test that would go looking, and
this paragraph is the whole of the disclosure.

## Running it the way the gate does

The gate's command, which needs Docker and a built tree. It is two runs, one per
server line, and each names the image that carries its runtime:

    dotnet restore
    dotnet build --configuration Release --no-restore -warnaserror
    for line in net9.0:9.0.316 net10.0:10.0.301; do
      docker run --rm --network none --user "$(id -u):$(id -g)" \
        --cap-drop ALL --security-opt no-new-privileges \
        -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp \
        -v "$PWD":"$PWD" -v "$HOME/.nuget/packages":"$HOME/.nuget/packages":ro \
        -w "$PWD" \
        "mcr.microsoft.com/dotnet/sdk:${line#*:}-noble-amd64" \
        dotnet test --configuration Release --no-build -f "${line%%:*}"
    done

The restore and the build are outside the container because a restore is a
network operation by definition. What has to hold with the network off is the
test run, and that is what runs inside.

`-f` is not optional here and dropping it is the mistake to avoid: without it the
run tries both legs in one image, and the leg whose runtime the image does not
carry ends with the testhost saying it must install .NET rather than with a test
failing.

The images are pinned by digest in the workflow rather than by the tags written
here, for the same reason the SDK versions are pinned: what judges one pull
request has to be what judges the next.

## On a machine without Docker

The suite runs the ordinary way and is not proving anything about the rule while
it does:

    dotnet test --configuration Release

That is the loop to work in. The container run is the gate's job, and a suite
that only ever ran on a developer's machine is the case this document exists
against.
