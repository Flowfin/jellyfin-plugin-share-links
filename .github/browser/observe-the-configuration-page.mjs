// Opens the configuration page in a real browser against a running Jellyfin (#349).
//
// The suite reads this page's TEXT and compares it with the compiled assembly:
// the routes it calls, the settings it names, the identifier it asks by. Every
// one of those tests was green on the day the page rendered with all eight fields
// blank and every button inert, because none of them runs the page in a client.
// `ConfigurationPageTests.TheControllerRunsWhereTheClientWillRunIt` closed the
// shape that caused it - a controller outside the element the client mounts - and
// a shape test is not the observation. This is the observation.
//
// What it asks is #349's own Done-when, in two halves. Opening the page shows the
// server's values in every field with no hand in the console, and a Save made on
// the page writes a value the API reads back. The second half is finished by the
// caller: this leg presses Save and waits for the server to answer it, and the
// shell script that runs this then re-reads the configuration over the API, so
// what proves the write is a route rather than the page's own report of itself.
//
// Everything is printed. Where a step asserts, it says so.

import { chromium } from "playwright";

const base = need("OBSERVE_BASE");
const operator = need("OBSERVE_OPERATOR");
const credential = need("OBSERVE_OPERATOR_CREDENTIAL");
const pluginId = need("OBSERVE_PLUGIN");
const held = JSON.parse(need("OBSERVE_CONFIGURATION"));
const saveSetting = need("OBSERVE_SAVE_SETTING");
const saveValue = need("OBSERVE_SAVE_VALUE");

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

// What the page should show for a value the server holds. The page writes an
// empty string for an absent value on purpose, because zero is a real answer for
// retention and a refused one for a ceiling, so the two may not be spelled the
// same way.
function asShown(value) {
    return value === null || value === undefined ? "" : String(value);
}

const browser = await chromium.launch();
const context = await browser.newContext({ baseURL: base });
const page = await context.newPage();

const configurationCalls = [];

page.on("console", (message) => {
    if (message.type() === "error") {
        process.stdout.write(`  browser console error: ${message.text()}\n`);
    }
});
page.on("response", (response) => {
    const url = response.url();
    if (url.includes("/ShareLinks/") || url.includes("AuthenticateByName") || url.includes(`/Plugins/`)) {
        process.stdout.write(`  the browser asked ${url} -> ${response.status()}\n`);
        if (url.includes("/Configuration")) {
            configurationCalls.push({ method: response.request().method(), status: response.status() });
        }
    }
});

try {
    say("the operator signs in on the server's own page");
    await page.goto(`${base}/web/index.html`, { waitUntil: "domcontentloaded" });

    // Which half of the sign-in pair the client opens on is the client's own
    // decision, so ask the page rather than assume it. This is the same shape the
    // guest leg uses and it is here for the same reason it is there.
    await page.waitForSelector("#txtManualName, .btnManual", { timeout: 60000 });
    if (!(await page.isVisible("#txtManualName"))) {
        await page.click(".btnManual");
    }
    await page.waitForSelector("#txtManualName", { state: "visible", timeout: 30000 });
    await page.fill("#txtManualName", operator);
    await page.fill("#txtManualPassword", credential);
    await page.click(".manualLoginForm button[type=submit]");

    await page
        .waitForFunction(() => !window.location.hash.includes("login"), null, { timeout: 60000 })
        .catch(async () => {
            const stillAt = await page.evaluate(() => window.location.hash);
            fail(`the client stayed on ${stillAt} after the operator's form was submitted, so nothing below would be about the page`);
        });
    process.stdout.write(`the client left the sign-in page for ${await page.evaluate(() => window.location.hash)}\n`);

    say("the operator opens the plugin's configuration page");
    // The client's own address for a plugin page, by the name the plugin
    // registers rather than by a path this job invents.
    await page.goto(`${base}/web/#/configurationpage?name=Share%20Links`, { waitUntil: "domcontentloaded" });

    const mounted = await page
        .waitForSelector("#ShareLinksConfigPage", { state: "attached", timeout: 60000 })
        .then(() => true)
        .catch(() => false);
    if (!mounted) {
        fail("the client never mounted #ShareLinksConfigPage, so the page an operator opens is not this plugin's");
    }
    process.stdout.write("the page element is in the document\n");

    // THE MEASUREMENT #349 WAS FOUND BY. On the shape that did not run this was
    // 0, because the client mounts the page element and inserts nothing else, so
    // a controller after that element's closing tag never entered the document.
    const scriptsInThePage = await page.evaluate(() => document.querySelectorAll("#ShareLinksConfigPage script").length);
    process.stdout.write(`scripts inside the page element: ${scriptsInThePage}\n`);

    // The page fills its fields from the server when the client shows it, so wait
    // for the request rather than for a length of time. A page that never asks is
    // the defect, and it has to be told apart from one that asked slowly.
    const asked = await page
        .waitForFunction((id) => window.performance.getEntriesByType("resource").some((entry) => entry.name.includes(`/Plugins/${id}/Configuration`)), pluginId, { timeout: 60000 })
        .then(() => true)
        .catch(() => false);
    if (!asked) {
        fail(`the page never asked the server for Plugins/${pluginId}/Configuration, so no field on it could be carrying a value the server holds`);
    }
    process.stdout.write("the page asked the server for its configuration\n");

    say("every field carries the value the server holds");
    const shown = await page.evaluate(() =>
        Array.prototype.slice.call(document.querySelectorAll("#ShareLinksConfigPage [data-setting]")).map((input) => ({
            setting: input.getAttribute("data-setting"),
            value: input.value,
        })),
    );

    if (shown.length === 0) {
        fail("the page carries no field declaring a setting, so there is nothing here to compare and a green line would mean nothing");
    }

    let wrong = 0;
    for (const field of shown) {
        const want = asShown(held[field.setting]);
        const agrees = field.value === want;
        process.stdout.write(`  ${field.setting}: page ${JSON.stringify(field.value)} server ${JSON.stringify(want)} ${agrees ? "" : "  <- DISAGREES"}\n`);
        if (!agrees) {
            wrong += 1;
        }
    }
    if (wrong > 0) {
        fail(`${wrong} of ${shown.length} fields do not carry the value the server holds, with no hand in the console. That is what #349 measured, and it read 8 of 8.`);
    }
    process.stdout.write(`OK: all ${shown.length} fields carry the server's values, with nothing typed into the console\n`);

    say("Save writes a value");
    // Changed on the page and submitted by the page's own button, because what is
    // under observation is the control an operator presses rather than a route
    // this job could call directly.
    const target = `#ShareLinksConfigPage [data-setting="${saveSetting}"]`;
    await page.fill(target, saveValue);
    process.stdout.write(`${saveSetting} typed as ${JSON.stringify(saveValue)} on the page\n`);

    const before = configurationCalls.filter((call) => call.method === "POST").length;
    await page.click("#ShareLinksSettingsForm button[type=submit]");

    // Waited for in this process rather than in the page, because what is being
    // waited for is a response the browser made and this is where those are
    // counted. A page that reported success without sending anything is exactly
    // the failure #349 is about, so the wait is on the request and never on a
    // message the page wrote about itself.
    let answered = false;
    for (let waited = 0; waited < 60 && !answered; waited += 1) {
        answered = configurationCalls.filter((call) => call.method === "POST").length > before;
        if (!answered) {
            await page.waitForTimeout(1000);
        }
    }

    if (!answered) {
        fail("pressing Save made no POST to the configuration route, so the button an operator presses sends nothing");
    }

    const wrote = configurationCalls.filter((call) => call.method === "POST").pop();
    process.stdout.write(`the page's Save answered ${wrote.status}\n`);
    if (![200, 204].includes(wrote.status)) {
        fail(`the server refused what the page's Save sent: ${wrote.status}`);
    }
    process.stdout.write("OK: the page's own Save reached the server and the server took it\n");
    process.stdout.write("what the API reads back is asserted by the caller of this script, not here\n");
} finally {
    await context.close();
    await browser.close();
}
