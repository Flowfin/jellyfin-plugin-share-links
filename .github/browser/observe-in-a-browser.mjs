// Opens the link in a real browser against a running Jellyfin (#237, #75).
//
// `docs/refused-tests.md` refuses a test that starts a real server and drives a
// browser, because the suite may reach neither. That refusal is about the suite.
// This is the observation job, which already brings a server up, so the browser
// is the one instrument left that can read what a guest actually meets when they
// click the link an operator sent them.
//
// Two claims in the tree have never been read by anything and are what this is
// pointed at. `ShareLinksGuestController.TheItemsAddress` says in its own remarks
// that the address it redirects to was not measured against a running web client.
// `docs/operator-guide.md` says the guest signs in and then opens the link. Both
// are about a browser, and no unit test can see either.
//
// Everything is printed. Where a step asserts, it says so; where the answer is
// recorded rather than judged, it says that too, because an observation nobody
// can read afterwards is not one.

import { chromium } from "playwright";

const base = need("OBSERVE_BASE");
const link = need("OBSERVE_LINK");
const guest = need("OBSERVE_GUEST");
const credential = need("OBSERVE_CREDENTIAL");
const sharedItem = need("OBSERVE_ITEM");
const otherItem = need("OBSERVE_OTHER_ITEM");

// The web client addresses an item by its identifier with the dashes taken out,
// which is the form `TheItemsAddress` builds and the form the server prints.
const sharedItemPlain = sharedItem.replaceAll("-", "").toLowerCase();
const otherItemPlain = otherItem.replaceAll("-", "").toLowerCase();

function need(name) {
    const value = process.env[name];
    if (!value) {
        fail(`${name} was not set, so there is nothing to observe`);
    }
    return value;
}

function say(what) {
    process.stdout.write(`\n----- ${what} -----\n`);
}

function fail(why) {
    process.stdout.write(`::error::${why}\n`);
    process.exit(1);
}

const browser = await chromium.launch();
const context = await browser.newContext({ baseURL: base });
const page = await context.newPage();

// The client's own console and every request it makes to this plugin, kept so a
// failure is read here rather than guessed at from a screenshot nobody took.
page.on("console", (message) => {
    if (message.type() === "error") {
        process.stdout.write(`  browser console error: ${message.text()}\n`);
    }
});
page.on("response", (response) => {
    if (response.url().includes("/ShareLinks/")) {
        process.stdout.write(`  the browser asked ${response.url()} -> ${response.status()}\n`);
    }
});

try {
    say("the guest signs in on the server's own page");
    await page.goto(`${base}/web/index.html`, { waitUntil: "domcontentloaded" });

    // The visual list is what the page opens on; the manual form is behind the
    // button below it and is the only way in for an account the list does not
    // show. Both are the client's own markup at the line this job pins.
    await page.waitForSelector(".btnManual", { timeout: 60000 });
    await page.click(".btnManual");
    await page.waitForSelector("#txtManualName", { state: "visible", timeout: 30000 });
    await page.fill("#txtManualName", guest);
    await page.fill("#txtManualPassword", credential);
    await page.click(".manualLoginForm button[type=submit]");

    await page.waitForFunction(() => !window.location.hash.includes("login"), null, {
        timeout: 60000,
    });
    process.stdout.write(`the client left the sign-in page for ${await page.evaluate(() => window.location.hash)}\n`);

    // Read back who the client believes it is, out of the client's own store
    // rather than out of anything this job put there.
    const signedInAs = await page.evaluate(() => {
        try {
            const raw = window.localStorage.getItem("jellyfin_credentials");
            const servers = raw ? JSON.parse(raw).Servers : [];
            const server = servers.find((candidate) => candidate.AccessToken);
            return server ? { userId: server.UserId, hasToken: true } : { hasToken: false };
        } catch (error) {
            return { hasToken: false, error: String(error) };
        }
    });
    process.stdout.write(`the client holds: ${JSON.stringify(signedInAs)}\n`);
    if (!signedInAs.hasToken) {
        fail("the guest could not sign in on the web client, so nothing below would be about the link");
    }
    process.stdout.write("OK: the guest signed in through the client's own form\n");

    say("the guest opens the link");
    // A top level navigation and not a call from inside the client, because that
    // is what clicking a link in a mail or a chat is. What the browser sends is
    // what the browser sends.
    const opened = await page.goto(link, { waitUntil: "domcontentloaded" });
    const openedStatus = opened ? opened.status() : "no response";
    process.stdout.write(`GET ${link} in the browser -> ${openedStatus}\n`);
    await page.waitForTimeout(3000);
    const landedOn = page.url();
    process.stdout.write(`the browser ended at ${landedOn}\n`);

    const reachedTheItem = landedOn.toLowerCase().includes(sharedItemPlain);
    if (reachedTheItem) {
        const shown = await page
            .waitForSelector(".nameContainer h1", { timeout: 60000 })
            .then((handle) => handle.textContent())
            .catch(() => null);
        process.stdout.write(`the client shows: ${shown === null ? "no item name" : shown.trim()}\n`);
        if (shown === null) {
            fail("the browser reached the address the redirect names and the client showed no item there, so the address in TheItemsAddress is not where the client shows one");
        }
        process.stdout.write("OK: the link opened in a browser reaches the item the share names\n");
    } else {
        process.stdout.write(`NOT OBSERVED: the browser did not reach the item. What it did is above, and the body it was given was ${JSON.stringify((await page.content()).slice(0, 300))}\n`);
    }

    say("the item no share of this guest names, addressed directly in the client");
    // The confinement seen through the client rather than through curl. The guest
    // is a signed in account on the operator's server, so the client will make
    // the call; what it may show is the question.
    await page.goto(`${base}/web/#/details?id=${otherItemPlain}`, { waitUntil: "domcontentloaded" });
    await page.waitForTimeout(5000);
    const shownForTheOther = await page
        .waitForSelector(".nameContainer h1", { timeout: 15000 })
        .then((handle) => handle.textContent())
        .catch(() => null);
    process.stdout.write(`the client shows for the other item: ${shownForTheOther === null ? "no item name" : shownForTheOther.trim()}\n`);
    if (shownForTheOther !== null && shownForTheOther.trim().length > 0) {
        fail("the client showed the guest an item no share of theirs names");
    }
    process.stdout.write("OK: the client showed the guest nothing for the item no share of theirs names\n");
} finally {
    await context.close();
    await browser.close();
}
