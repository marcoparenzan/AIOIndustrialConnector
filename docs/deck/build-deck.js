// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.
//
// Generates docs/AIO-Industrial-Connector.pptx (shared architecture, Part 1 Modbus, Part 2 S7) with pptxgenjs.
// Dependencies (pptxgenjs, react, react-dom, react-icons, sharp) are installed outside the source tree, in any
// folder <dir> (the maintainer uses D:\Dev\BuildArtifacts\AIOModbusConnector\tmp\pptx):
//   cd <dir> && npm install pptxgenjs react react-dom react-icons sharp jszip
// Run from the repository root:
//   $env:NODE_PATH="<dir>\node_modules"; node docs/deck/build-deck.js

const path = require("path");
const pptxgen = require("pptxgenjs");
const React = require("react");
const ReactDOMServer = require("react-dom/server");
const sharp = require("sharp");
const fa = require("react-icons/fa");

const OUT = path.resolve(__dirname, "..", "AIO-Industrial-Connector.pptx");
const APPLY_THEME = process.env.APPLY_THEME_JS; // path to the pptx skill's apply_theme.js (optional)

// ---------- Theme: graphite + signal amber (industrial control room) ----------
const THEME = {
  name: "AIO Industrial",
  headFontFace: "Cambria",
  bodyFontFace: "Calibri",
  colors: {
    dk1: "1B2329", // text
    lt1: "FFFFFF",
    dk2: "1F2A30", // dark backgrounds
    lt2: "EEF2F3", // light tint
    accent1: "F2A541", // amber: the connector, highlights
    accent2: "2A9D8F", // teal: AIO / Azure
    accent3: "E76F51", // coral: warnings
    accent4: "5C7A89", // slate: muted
    accent5: "6A994E", // green: verified
    accent6: "264653", // deep teal
    hlink: "2A9D8F",
    folHlink: "5C7A89",
  },
};
const HEX = THEME.colors;

const pres = new pptxgen();
pres.layout = "LAYOUT_WIDE"; // 13.33 x 7.5 in
pres.author = "Marco Parenzan";
pres.title = "AIO Industrial Connector: Modbus TCP and Siemens S7";
pres.subject = "Custom Akri connector for Azure IoT Operations";
pres.theme = { headFontFace: THEME.headFontFace, bodyFontFace: THEME.bodyFontFace };
const C = pres.SchemeColor;

const W = 13.333;
const MX = 0.6; // horizontal margin
const CW = W - 2 * MX; // content width
const TOP = 1.55; // content top on content slides
const MONO = "Courier New";

// ---------- Layouts ----------
pres.defineSlideMaster({
  title: "TITLE_DARK",
  background: { color: C.text2 },
  objects: [
    { placeholder: { options: { name: "title", type: "title", x: MX, y: 2.0, w: 11.5, h: 1.5, fontSize: 48, bold: true, color: C.background1, fontFace: THEME.headFontFace, valign: "bottom", align: "left", margin: 0 }, text: "" } },
    { placeholder: { options: { name: "body", type: "body", x: MX, y: 3.65, w: 11.0, h: 1.1, fontSize: 22, color: C.accent1, valign: "top", align: "left", margin: 0 }, text: "" } },
  ],
});

pres.defineSlideMaster({
  title: "SECTION_DARK",
  background: { color: C.text2 },
  objects: [
    { placeholder: { options: { name: "title", type: "title", x: 2.2, y: 2.75, w: 10.3, h: 1.2, fontSize: 40, bold: true, color: C.background1, fontFace: THEME.headFontFace, valign: "bottom", align: "left", margin: 0 }, text: "" } },
    { placeholder: { options: { name: "body", type: "body", x: 2.2, y: 4.05, w: 10.3, h: 0.9, fontSize: 20, color: C.background2, valign: "top", align: "left", margin: 0 }, text: "" } },
  ],
  slideNumber: { x: 12.2, y: 6.95, w: 0.6, h: 0.3, fontSize: 10, color: C.accent4, align: "right" },
});

pres.defineSlideMaster({
  title: "CONTENT",
  background: { color: C.background1 },
  margin: [0.5, 0.6, 0.7, 0.6],
  objects: [
    { placeholder: { options: { name: "title", type: "title", x: MX, y: 0.45, w: CW, h: 0.85, fontSize: 36, bold: true, color: C.text1, fontFace: THEME.headFontFace, valign: "middle", align: "left", margin: 0 }, text: "" } },
    { text: { text: "AIO Industrial Connector  ·  v0.2.0 preview", options: { x: MX, y: 6.95, w: 6, h: 0.3, fontSize: 10, color: C.accent4, margin: 0 } } },
  ],
  slideNumber: { x: 12.2, y: 6.95, w: 0.6, h: 0.3, fontSize: 10, color: C.accent4, align: "right" },
});

// ---------- Helpers ----------
const iconCache = new Map();
async function iconPng(name, hex) {
  const key = `${name}-${hex}`;
  if (!iconCache.has(key)) {
    const svg = ReactDOMServer.renderToStaticMarkup(React.createElement(fa[name], { color: `#${hex}`, size: 256 }));
    const buf = await sharp(Buffer.from(svg)).resize(256, 256).png().toBuffer();
    iconCache.set(key, "image/png;base64," + buf.toString("base64"));
  }
  return iconCache.get(key);
}

/** Icon in a filled circle: the deck's visual motif. */
async function iconCircle(slide, name, x, y, d, fill, iconHex = HEX.lt1, label = name) {
  slide.addShape(pres.shapes.OVAL, { x, y, w: d, h: d, fill: { color: fill }, line: { type: "none" }, objectName: `${label} circle` });
  const pad = d * 0.25;
  slide.addImage({ data: await iconPng(name, iconHex), x: x + pad, y: y + pad, w: d - 2 * pad, h: d - 2 * pad, altText: label, objectName: `${label} icon` });
}

function shadow() {
  return { type: "outer", color: "000000", opacity: 0.12, blur: 6, offset: 2, angle: 90 };
}

function card(slide, x, y, w, h, name, fill = C.background2) {
  slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w, h, rectRadius: 0.08, fill: { color: fill }, line: { type: "none" }, shadow: shadow(), objectName: name });
}

function text(slide, t, opts) {
  slide.addText(t, { isTextBox: true, margin: 0, fontSize: 16, color: C.text1, valign: "top", ...opts });
}

function code(slide, lines, x, y, w, h, opts = {}) {
  const dark = opts.dark ?? false;
  slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w, h, rectRadius: 0.06, fill: { color: dark ? C.text2 : C.background2 }, line: { type: "none" }, objectName: opts.name ?? "code block" });
  slide.addText(lines.join("\n"), {
    isTextBox: true, x: x + 0.2, y: y + 0.15, w: w - 0.4, h: h - 0.3, margin: 0, valign: "top",
    fontFace: MONO, fontSize: opts.fontSize ?? 14, color: dark ? C.background2 : C.text1, objectName: (opts.name ?? "code block") + " text",
  });
}

function arrow(slide, x1, y1, x2, y2, color = C.accent4, name = "arrow") {
  slide.addShape(pres.shapes.LINE, {
    x: Math.min(x1, x2), y: Math.min(y1, y2), w: Math.abs(x2 - x1), h: Math.abs(y2 - y1),
    flipH: x2 < x1, flipV: y2 < y1, line: { color, width: 2, endArrowType: "triangle" }, objectName: name,
  });
}

function bullets(items, size = 16) {
  return items.map((t, i) => ({ text: t, options: { bullet: true, breakLine: i < items.length - 1, paraSpaceAfter: 6, fontSize: size } }));
}

function table(slide, header, rows, x, y, w, colW, opts = {}) {
  const fs = opts.fontSize ?? 14;
  const border = { type: "solid", pt: 0.75, color: "D5DCDF" };
  const data = [
    header.map((h) => ({ text: h, options: { bold: true, color: C.background1, fill: { color: C.text2 }, fontSize: fs } })),
    ...rows.map((r, ri) => r.map((c, ci) => ({
      text: c,
      options: {
        fontSize: fs, color: C.text1, fill: { color: ri % 2 ? C.background2 : C.background1 },
        fontFace: opts.monoCols?.includes(ci) ? MONO : undefined, bold: opts.boldCols?.includes(ci) ?? false,
      },
    }))),
  ];
  slide.addTable(data, { x, y, w, colW, border, margin: [0.04, 0.08, 0.04, 0.08], valign: "middle", rowH: opts.rowH ?? 0.36, objectName: opts.name ?? "table" });
}

function contentSlide(section, title) {
  const s = pres.addSlide({ masterName: "CONTENT", sectionTitle: section });
  s.addText(title, { placeholder: "title" });
  return s;
}

async function sectionSlide(section, title, subtitle, iconName) {
  pres.addSection({ title: section });
  const s = pres.addSlide({ masterName: "SECTION_DARK", sectionTitle: section });
  s.addText(title, { placeholder: "title" });
  s.addText(subtitle, { placeholder: "body" });
  await iconCircle(s, iconName, 0.6, 2.95, 1.3, C.accent1, HEX.dk2, section);
  return s;
}

// ---------- Slides ----------
async function build() {
  // 1. Title
  pres.addSection({ title: "Introduction" });
  {
    const s = pres.addSlide({ masterName: "TITLE_DARK", sectionTitle: "Introduction" });
    s.addText("AIO Industrial Connector", { placeholder: "title" });
    s.addText("One codebase, one executable, two protocol modes for Azure IoT Operations: Modbus TCP and Siemens S7", { placeholder: "body" });
    const y = 5.35;
    await iconCircle(s, "FaIndustry", MX, y, 0.8, C.accent4, HEX.lt1, "Industrial device");
    arrow(s, MX + 0.95, y + 0.4, MX + 1.75, y + 0.4, C.accent1, "device to connector");
    await iconCircle(s, "FaPlug", MX + 1.9, y, 0.8, C.accent1, HEX.dk2, "Connector");
    arrow(s, MX + 2.85, y + 0.4, MX + 3.65, y + 0.4, C.accent1, "connector to AIO");
    await iconCircle(s, "FaCloud", MX + 3.8, y, 0.8, C.accent2, HEX.lt1, "Azure IoT Operations");
    text(s, "Marco Parenzan  ·  November 2026  ·  v0.2.0 preview", { x: 7.2, y: y + 0.2, w: 5.5, h: 0.4, fontSize: 14, color: C.background2, align: "right" });
    s.addNotes("This deck explains the industrial connector for Azure IoT Operations. The shared architecture comes first, then Part 1, the Modbus TCP mode, and Part 2, the Siemens S7 mode, each with configuration, deployment and local simulation. It closes with the honest status: what has been verified and what has not.");
  }
  // 2. In a nutshell
  {
    const s = contentSlide("Introduction", "In a nutshell");
    const stats = [
      ["2", "protocol modes", "Modbus TCP and Siemens S7, one mode per instance"],
      ["1", "shared core", "planning, sampling, schema and pooling written once"],
      ["1", "connection", "per device, shared by every dataset"],
      ["159", "automated tests", "Core 14 · Modbus 72 · S7 73, end to end over TCP"],
    ];
    const cw = (CW - 3 * 0.3) / 4;
    stats.forEach(([n, label, desc], i) => {
      const x = MX + i * (cw + 0.3);
      card(s, x, TOP + 0.1, cw, 2.6, `stat card ${label}`);
      text(s, n, { x: x + 0.3, y: TOP + 0.3, w: cw - 0.6, h: 1.1, fontSize: 64, bold: true, color: i === 3 ? C.accent5 : C.accent1, fontFace: THEME.headFontFace });
      text(s, label, { x: x + 0.3, y: TOP + 1.4, w: cw - 0.6, h: 0.4, fontSize: 18, bold: true });
      text(s, desc, { x: x + 0.3, y: TOP + 1.85, w: cw - 0.6, h: 0.75, fontSize: 14, color: C.accent4 });
    });
    text(s, [
      { text: "Polls Modbus and S7 devices and publishes each asset dataset as JSON ", options: {} },
      { text: "to the AIO MQTT broker or state store, ", options: {} },
      { text: "configured entirely from Azure Device Registry and the operations experience.", options: { bold: true } },
    ], { x: MX, y: 4.75, w: CW, h: 0.9, fontSize: 20 });
    text(s, ".NET 10 · Azure IoT Operations connector SDK 1.3 · NModbus 3 · S7netplus 0.20 · MIT licence", { x: MX, y: 5.8, w: CW, h: 0.4, fontSize: 14, italic: true, color: C.accent4 });
    s.addNotes("Four numbers summarize the scope. Two protocol modes, one shared core, one shared connection per device, and one hundred fifty-nine automated tests. The key point: no configuration lives in the image; everything is set in Azure Device Registry, so the portal and the CLI are the only tools an operator needs.");
  }
  // 3. Why
  {
    const s = contentSlide("Introduction", "Why two protocols, one codebase");
    const colW = (CW - 0.5) / 2;
    const rows = [
      [
        ["FaIndustry", C.accent4, "Modbus is everywhere", "PLCs, energy meters, drives and RTU gateways on most shop floors still speak Modbus."],
        ["FaPuzzlePiece", C.accent3, "S7 runs Siemens plants", "S7-300/400/1200/1500 PLCs are the installed base in discrete manufacturing. AIO has no built-in connector for them or for Modbus."],
      ],
      [
        ["FaPlug", C.accent1, "Custom Akri connector", "Built on the AIO .NET connector SDK; same lifecycle, status and health as built-in connectors. Configured in Azure, no image rebuild."],
        ["FaCubes", C.accent2, "One shared core", "Adding S7 reused the sampling pipeline, planner, schema and pooling unchanged: only addresses, decoding and the client are new."],
      ],
    ];
    const heads = ["The gap", "The approach"];
    for (let c = 0; c < 2; c++) {
      const x = MX + c * (colW + 0.5);
      text(s, heads[c], { x, y: TOP, w: colW, h: 0.45, fontSize: 22, bold: true, color: c === 0 ? C.accent4 : C.accent2, fontFace: THEME.headFontFace });
      for (let r = 0; r < 2; r++) {
        const [icon, fill, head, body] = rows[c][r];
        const y = TOP + 0.75 + r * 2.15;
        card(s, x, y, colW, 1.9, `${head} card`);
        await iconCircle(s, icon, x + 0.3, y + 0.35, 0.9, fill, HEX.lt1, head);
        text(s, head, { x: x + 1.45, y: y + 0.3, w: colW - 1.7, h: 0.45, fontSize: 20, bold: true });
        text(s, body, { x: x + 1.45, y: y + 0.8, w: colW - 1.7, h: 0.95, fontSize: 16 });
      }
    }
    s.addNotes("The gap: Modbus is the lingua franca of field devices and S7 is the installed base of Siemens plants, but the list of Azure IoT Operations connectors has OPC UA, REST, MQTT, SSE, ONVIF and media, not these two. The approach: custom Akri connectors, so both get the same treatment as first-party ones. And one shared core, so the second protocol is a module, not a second project.");
  }
  // 4. Section: Architecture
  await sectionSlide("Architecture", "Shared architecture", "Where the connector runs, what is shared, what is per protocol", "FaSitemap").then((s) =>
    s.addNotes("First, the architecture shared by both modes: how the connector fits into Azure IoT Operations, and how it is organized so that a protocol is a small module."));
  // 5. How it fits
  {
    const s = contentSlide("Architecture", "How it fits in Azure IoT Operations");
    const y0 = TOP + 0.05, zh = 4.9;
    const zones = [
      [MX, 3.6, "Azure", C.accent2],
      [MX + 3.95, 5.3, "Edge cluster (Arc-enabled Kubernetes)", C.accent6],
      [MX + 9.6, 2.53, "Shop floor", C.accent4],
    ];
    zones.forEach(([x, w, label, color]) => {
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y: y0, w, h: zh, rectRadius: 0.1, fill: { color: C.background2 }, line: { type: "none" }, objectName: `${label} zone` });
      text(s, label, { x: x + 0.25, y: y0 + 0.15, w: w - 0.5, h: 0.4, fontSize: 16, bold: true, color });
    });
    const box = (x, y, w, h, title, sub, fill, fg = C.background1, name = title) => {
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w, h, rectRadius: 0.08, fill: { color: fill }, line: { type: "none" }, shadow: shadow(), objectName: name });
      text(s, [{ text: title, options: { bold: true, breakLine: true, fontSize: 16 } }, { text: sub, options: { fontSize: 14 } }],
        { x: x + 0.15, y: y + 0.08, w: w - 0.3, h: h - 0.16, color: fg, valign: "middle" });
    };
    const cx = MX + 0.25, cw = 3.1;
    box(cx, y0 + 0.7, cw, 1.15, "AIO instance", "one template per protocol", C.accent2);
    box(cx, y0 + 2.05, cw, 1.15, "Device Registry", "device · endpoint · asset", C.accent2);
    box(cx, y0 + 3.4, cw, 1.15, "Container Registry", "image + metadata artifact", C.accent2);
    const ex = MX + 4.2, ew = 2.3;
    box(ex, y0 + 0.7, ew, 1.15, "Akri operator", "starts connector pods", C.accent6);
    box(ex + 2.55, y0 + 0.7, ew, 1.15, "ADR service", "device/asset updates", C.accent6);
    box(ex + 1.2, y0 + 2.2, 2.6, 1.15, "Connector pod", "one protocol mode", C.accent1, C.text1, "connector pod");
    box(ex, y0 + 3.55, 4.85, 1.0, "MQTT broker · state store · schema registry", "JSON payloads and JSON Schema", C.accent6);
    box(MX + 9.85, y0 + 2.2, 2.03, 1.15, "PLC / meter", "Modbus TCP · S7", C.accent4);
    arrow(s, cx + cw, y0 + 1.27, ex, y0 + 1.27, C.accent2, "template sync");
    arrow(s, ex + 1.15, y0 + 1.85, ex + 1.8, y0 + 2.2, C.accent4, "operator creates pod");
    arrow(s, ex + 3.7, y0 + 1.85, ex + 3.2, y0 + 2.2, C.accent4, "ADR notifies connector");
    arrow(s, ex + 3.8, y0 + 2.78, MX + 9.85, y0 + 2.78, C.accent1, "device reads");
    arrow(s, ex + 2.5, y0 + 3.35, ex + 2.5, y0 + 3.55, C.accent1, "publish JSON");
    text(s, "Modbus · S7", { x: ex + 3.95, y: y0 + 2.35, w: 1.5, h: 0.35, fontSize: 14, bold: true, color: C.accent1 });
    s.addNotes("In Azure: the AIO instance holds the connector template, Device Registry holds devices and assets, and the container registry holds one image and one metadata artifact per protocol. On the edge, the Akri operator starts the connector pod from the template; the ADR service tells the connector which devices and assets exist. The connector reads the device with Modbus function codes 1 to 4 or S7 read-variable requests, depending on its mode, and publishes JSON to the broker or the state store, and registers a JSON Schema per dataset.");
  }
  // 6. Inside the connector
  {
    const s = contentSlide("Architecture", "Inside the connector: one pipeline");
    const steps = [
      ["FaCogs", "Sampler factory", "Template: protocol supplies parsing hooks"],
      ["FaClipboardList", "Dataset plan", "Validates every point, collects all errors"],
      ["FaCompressArrowsAlt", "Range planner", "Merges adjacent points per table or data block"],
      ["FaNetworkWired", "Client", "NModbus or S7netplus, shared, reconnects"],
      ["FaFileCode", "Decoder + JSON", "Protocol decoding, shared numeric core"],
    ];
    const gap = 0.35, cw = (CW - 4 * gap) / 5, y = TOP + 0.3, h = 3.2;
    for (let i = 0; i < steps.length; i++) {
      const [icon, head, body] = steps[i];
      const x = MX + i * (cw + gap);
      card(s, x, y, cw, h, `${head} card`);
      await iconCircle(s, icon, x + (cw - 0.9) / 2, y + 0.3, 0.9, i === 3 ? C.accent2 : C.accent1, i === 3 ? HEX.lt1 : HEX.dk2, head);
      text(s, head, { x: x + 0.15, y: y + 1.35, w: cw - 0.3, h: 0.45, fontSize: 18, bold: true, align: "center" });
      text(s, body, { x: x + 0.2, y: y + 1.85, w: cw - 0.4, h: 1.2, fontSize: 15, color: C.accent4, align: "center" });
      if (i < steps.length - 1) arrow(s, x + cw + 0.04, y + h / 2, x + cw + gap - 0.04, y + h / 2, C.accent4, `step ${i + 1} arrow`);
    }
    text(s, [
      { text: "The SDK does the rest: ", options: { bold: true } },
      { text: "PollingTelemetryConnectorWorker runs one timer per dataset, forwards payloads to MQTT or the state store, reports device and asset status, and handles leader election." },
    ], { x: MX, y: 5.2, w: CW, h: 0.9, fontSize: 16 });
    s.addNotes("Five pieces of our code. The factory parses all configuration levels. The plan validates every data point and collects every error at once. The planner merges adjacent registers into as few requests as possible. The client wraps NModbus or S7netplus with one shared, serialized connection per device. The decoder applies data types, protocol-specific ordering, scale and bits, and the sampler writes a flat JSON object. Every box is shared code except the protocol hooks it calls. Timers, publishing, status and leader election come from the SDK.");
  }
  // 7. Resilient by design
  {
    const s = contentSlide("Architecture", "Resilient by design");
    const items = [
      ["FaNetworkWired", C.accent2, "One connection per device", "Modbus servers accept 1 to 4 connections, S7 CPUs a small fixed number. Datasets and assets share one client; requests are serialized."],
      ["FaCompressArrowsAlt", C.accent1, "Fewer, larger requests", "Adjacent points are merged per Modbus table or S7 data block, within the request limit. Bridging gaps is opt-in (maxGap)."],
      ["FaPuzzlePiece", C.accent3, "Partial results", "A rejected range (Modbus exception, S7 PLC error) publishes null for its points; the rest is still published."],
      ["FaShieldAlt", C.accent5, "Fail fast, explain everything", "Configuration errors are collected and shown on the device status; connection loss retries once."],
    ];
    const cw = (CW - 0.4) / 2, ch = 2.15;
    for (let i = 0; i < 4; i++) {
      const [icon, fill, head, body] = items[i];
      const x = MX + (i % 2) * (cw + 0.4), y = TOP + Math.floor(i / 2) * (ch + 0.35);
      card(s, x, y, cw, ch, `${head} card`);
      await iconCircle(s, icon, x + 0.3, y + 0.35, 0.9, fill, HEX.lt1, head);
      text(s, head, { x: x + 1.45, y: y + 0.3, w: cw - 1.7, h: 0.45, fontSize: 20, bold: true });
      text(s, body, { x: x + 1.45, y: y + 0.82, w: cw - 1.7, h: 1.2, fontSize: 16 });
    }
    s.addNotes("Four behaviours matter in the field. One shared connection per device, because Modbus servers and S7 CPUs have very few connection slots. Request merging, with gap bridging off by default because unmapped addresses often raise an exception. Partial results, so one bad range never hides all other values. And configuration errors reported together on the device status, because operators in the portal do not see pod logs.");
  }
  // A1. Core vs protocol
  {
    const s = contentSlide("Architecture", "What is shared, what is per protocol");
    table(s, ["Concern", "Core (written once)", "Per protocol"], [
      ["Settings cascade", "IProtocolSettings, JSON reading", "the settings record"],
      ["Plan + validation", "DatasetPlanner: all errors at once", "address and type rules, limits"],
      ["Request merging", "RangePlanner", "grouping key, size limit"],
      ["Sampling", "retry once, partial results, JSON", "BlockReader: read + decode a block"],
      ["Connection", "lazy connect, reset, serialize, pool", "connect, call, classify errors"],
      ["Schema + factory", "template classes, JSON Schema", "parse hooks"],
      ["Hosting", "IProtocolModule, ProtocolCatalog", "one small Protocol class"],
    ], MX, TOP + 0.05, 8.3, [2.0, 3.4, 2.9], { rowH: 0.52, fontSize: 14, boldCols: [0], name: "core vs protocol table" });
    card(s, MX + 8.7, TOP + 0.05, CW - 8.7, 4.2, "dependency card");
    text(s, "Dependency rule", { x: MX + 8.95, y: TOP + 0.25, w: CW - 9.2, h: 0.4, fontSize: 18, bold: true });
    text(s, [
      { text: "Core knows no protocol.", options: { breakLine: true } },
      { text: "A module knows Core and its own library.", options: { breakLine: true } },
      { text: "Modules never reference each other.", options: { breakLine: true } },
      { text: "Only the host and the probe reference several modules.", options: { breakLine: true } },
    ], { x: MX + 8.95, y: TOP + 0.8, w: CW - 9.2, h: 2.3, fontSize: 15 });
    text(s, "Adding a protocol never touches the existing ones.", { x: MX + 8.95, y: TOP + 3.2, w: CW - 9.2, h: 0.9, fontSize: 15, bold: true, color: C.accent1 });
    text(s, "Folders: src/Core · src/Modbus · src/S7 · src/Host · src/Tools, mirrored in tests, docs and deploy.", { x: MX, y: TOP + 4.5, w: CW, h: 0.5, fontSize: 14, italic: true, color: C.accent4 });
    s.addNotes("The refactoring in one table. The left column is code that used to live in the Modbus connector and is now written once: the settings cascade, the plan builder that collects every error, the request merger, the sampler with its retry and partial-result policy, the connection lifecycle, the schema provider and the hosting. The right column is what a protocol must provide. The dependency rule keeps it that way: modules never reference each other, so adding a protocol never touches an existing one. Folders mirror this in source, tests, docs and deploy.");
  }

  // A2. Patterns
  {
    const s = contentSlide("Architecture", "Design patterns, kept simple");
    const items = [
      ["FaGem", C.accent1, "Facade", "IProtocolModule: the host sees name, endpoint type, lock name and AddServices. Everything behind it is private to the module."],
      ["FaProjectDiagram", C.accent2, "Template method", "DatasetPlanner, sampler factory, schema provider, connection base: skeleton fixed in Core, protocols override only the differing steps."],
      ["FaExchangeAlt", C.accent6, "Strategy + adapter", "IBlockReader picks how a block is read and decoded. NModbus and S7netplus sit behind small client interfaces and map their exceptions."],
      ["FaBook", C.accent4, "Registry + pool", "ProtocolCatalog resolves a mode name to its module. ClientPool shares one connection per device."],
    ];
    const cw = (CW - 0.4) / 2, ch = 2.1;
    for (let i = 0; i < 4; i++) {
      const [icon, fill, head, body] = items[i];
      const x = MX + (i % 2) * (cw + 0.4), y = TOP + Math.floor(i / 2) * (ch + 0.3);
      card(s, x, y, cw, ch, `${head} card`);
      await iconCircle(s, icon, x + 0.3, y + 0.35, 0.9, fill, i === 0 ? HEX.dk2 : HEX.lt1, head);
      text(s, head, { x: x + 1.45, y: y + 0.28, w: cw - 1.7, h: 0.45, fontSize: 20, bold: true });
      text(s, body, { x: x + 1.45, y: y + 0.8, w: cw - 1.7, h: 1.2, fontSize: 15 });
    }
    text(s, "Deliberately not shared: a common settings type or data model. Modbus has byte/word order and unit ids, S7 has racks, slots and address widths.", { x: MX, y: TOP + 4.65, w: CW, h: 0.6, fontSize: 14, italic: true, color: C.accent4 });
    s.addNotes("Four patterns, each used where it removes duplication or hides a dependency. Facade: the host stays a few lines. Template method: the skeleton of planning, factory, schema and connection handling exists once. Strategy and adapter: the sampler is protocol neutral, and the third-party libraries are hidden behind small interfaces. Registry and pool: mode selection and connection sharing. What is deliberately not shared is the settings and data model, because forcing one shape would make both protocols worse.");
  }

  // A3. One executable, two modes
  {
    const s = contentSlide("Architecture", "One executable, one mode per instance");
    code(s, [
      "# argument or environment variable",
      "IndustrialConnector --protocol s7",
      "CONNECTOR_PROTOCOL=modbus",
      "",
      "# no mode, or an unknown one: exit with an error",
      "No protocol selected. Available: modbus, s7.",
    ], MX, TOP, 6.2, 2.5, { dark: true, fontSize: 14, name: "mode block" });
    table(s, ["", "Modbus", "S7"], [
      ["Image", "aio-modbus-connector", "aio-s7-connector"],
      ["Endpoint type", "Custom.ModbusTcp", "Custom.SiemensS7"],
      ["Template", "modbus-tcp-connector", "s7-connector"],
      ["Leader lock", "aio-modbus-connector-leader", "aio-s7-connector-leader"],
    ], MX + 6.6, TOP, CW - 6.6, [1.5, 2.1, 1.9], { rowH: 0.5, fontSize: 13, boldCols: [0], name: "mode table" });
    card(s, MX, TOP + 2.9, CW, 1.9, "why one image card");
    await iconCircle(s, "FaCodeBranch", MX + 0.3, TOP + 3.2, 0.9, C.accent2, HEX.lt1, "single build");
    text(s, [
      { text: "One build, two images. ", options: { bold: true } },
      { text: "The mode is baked into each image (CONNECTOR_PROTOCOL), so a connector template needs no extra settings. Templates, pods, replicas, leader locks and failure domains stay per protocol; the mode is never guessed from ADR data." },
    ], { x: MX + 1.45, y: TOP + 3.1, w: CW - 1.75, h: 1.5, fontSize: 16, valign: "middle" });
    s.addNotes("One executable, one protocol per instance. The mode is chosen with the protocol argument or the CONNECTOR_PROTOCOL variable. The published images bake the variable in, so there are two images, two metadata artifacts and two connector templates that never interact. The mode is deliberately not inferred from the device data, so a misconfigured endpoint type can never make a pod poll the wrong protocol.");
  }

  // A4. Outside AIO
  {
    const s = contentSlide("Architecture", "Planned: run outside AIO (bare metal)");
    table(s, ["Part", "Content", "Effort"], [
      ["Configuration source", "devices and assets from YAML/JSON files, same schema as ADR", "~1 day"],
      ["Run loop", "one timer per dataset, shutdown, errors to log", "~0.5 day"],
      ["Sink", "console/file; plain MQTT broker with TLS and reconnect", "0.5 / 1-2 days"],
      ["Packaging", "Windows service or systemd, self-contained publish", "~0.5-1 day"],
      ["Tests + docs", "end to end with the simulators", "~1 day"],
    ], MX, TOP + 0.05, CW, [2.6, 7.2, 2.3], { rowH: 0.55, fontSize: 14, boldCols: [0], name: "standalone effort table" });
    card(s, MX, TOP + 3.5, CW, 1.45, "standalone note card");
    await iconCircle(s, "FaDesktop", MX + 0.3, TOP + 3.75, 0.85, C.accent4, HEX.lt1, "bare metal");
    text(s, [
      { text: "About 4-6 days, not started. ", options: { bold: true } },
      { text: "The sampling pipeline does not depend on MQTT, ADR or Akri, and the probe already runs it from the same manifests. Open: how configuration is managed outside AIO (files, database, API)." },
    ], { x: MX + 1.4, y: TOP + 3.65, w: CW - 1.7, h: 1.2, fontSize: 15, valign: "middle" });
    s.addNotes("The process is meant to run outside Azure IoT Operations too, for flexible installation and testing. Today the probe already runs the sampling code anywhere, but the connector host needs the AIO environment. A standalone host needs three replaceable parts around the same core: a configuration source, a run loop and a sink. The estimate is four to six days for file configuration with console or MQTT output. Central configuration management, schema registry and remote status are not included, and the configuration approach outside AIO is the open decision.");
  }
  // 8. Section: Part 1 Modbus
  await sectionSlide("Modbus", "Part 1 · Modbus TCP", "Registers, byte order, deployment and simulation", "FaMicrochip").then((s) =>
    s.addNotes("Part one: the Modbus TCP mode. First how registers are described, then deployment, then local simulation."));
  // 9. Cascade
  {
    const s = contentSlide("Modbus", "Modbus: one JSON shape, four levels");
    const levels = [
      ["Device endpoint", "additionalConfiguration", "unitId, timeouts, byte/word order"],
      ["Asset", "defaultDatasetsConfiguration", "defaults for every dataset"],
      ["Dataset", "datasetConfiguration", "samplingIntervalMs, request sizing"],
      ["Data point", "dataPointConfiguration", "dataType, scale, bit, length"],
    ];
    const fills = [C.accent4, C.accent6, C.accent2, C.accent1];
    for (let i = 0; i < 4; i++) {
      const [lvl, field, what] = levels[i];
      const x = MX + i * 0.45, y = TOP + i * 1.12, w = 7.4 - i * 0.45;
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w, h: 0.95, rectRadius: 0.08, fill: { color: fills[i] }, line: { type: "none" }, objectName: `${lvl} level` });
      text(s, [{ text: lvl, options: { bold: true, fontSize: 18, breakLine: true } }, { text: `${field}  ·  ${what}`, options: { fontSize: 14 } }],
        { x: x + 0.25, y: y + 0.08, w: w - 0.5, h: 0.8, color: i === 3 ? C.text1 : C.background1, valign: "middle" });
    }
    arrow(s, MX + 7.75, TOP + 0.2, MX + 7.75, TOP + 4.2, C.accent1, "specificity arrow");
    text(s, "more specific wins", { x: MX + 7.95, y: TOP + 1.9, w: 1.6, h: 0.7, fontSize: 16, bold: true, color: C.accent1 });
    card(s, MX + 9.4, TOP, CW - 9.4, 4.4, "example card");
    text(s, "Example", { x: MX + 9.65, y: TOP + 0.2, w: 2.5, h: 0.4, fontSize: 18, bold: true });
    text(s, [
      { text: "unitId: 2", options: { fontFace: MONO, bold: true } },
      { text: " on the endpoint applies to every register of every asset on that device, unless a dataset or a single data point sets its own " },
      { text: "unitId", options: { fontFace: MONO } },
      { text: "." },
    ], { x: MX + 9.65, y: TOP + 0.75, w: CW - 9.9, h: 2.2, fontSize: 16 });
    text(s, "Same property names at every level; settings that make no sense at a level are ignored.", { x: MX + 9.65, y: TOP + 3.0, w: CW - 9.9, h: 1.2, fontSize: 14, italic: true, color: C.accent4 });
    s.addNotes("Every configuration field in Device Registry is a JSON string. The connector reads the same shape at four levels: device endpoint, asset default, dataset, data point. The most specific level wins. The S7 mode uses the same cascade with its own properties. That keeps the mental model simple: set device-wide defaults like unit id or word order once on the endpoint, override only where needed.");
  }
  // 10. Addressing
  {
    const s = contentSlide("Modbus", "Modbus: addressing a register");
    table(s, ["dataSource", "Table", "Function code", "Base"], [
      ["hr:0  holding:0  4x:0", "Holding register", "3", "zero-based"],
      ["ir:10  input:10  3x:10", "Input register", "4", "zero-based"],
      ["coil:3  co:3  0x:3", "Coil", "1", "zero-based"],
      ["di:7  discrete:7  1x:7", "Discrete input", "2", "zero-based"],
      ["40001 / 400001", "Holding register 0", "3", "one-based (Modicon)"],
      ["30010 · 10001 · 00005", "ir:9 · di:0 · coil:4", "4 · 2 · 1", "one-based (Modicon)"],
    ], MX, TOP + 0.05, 8.2, [2.9, 2.2, 1.4, 1.7], { monoCols: [0], rowH: 0.5, name: "address table" });
    card(s, MX + 8.6, TOP + 0.05, CW - 8.6, 3.55, "off by one card");
    await iconCircle(s, "FaExclamationTriangle", MX + 8.85, TOP + 0.3, 0.8, C.accent3, HEX.lt1, "warning");
    text(s, "hr:0 = 40001", { x: MX + 9.8, y: TOP + 0.42, w: 2.6, h: 0.55, fontSize: 22, bold: true, fontFace: MONO });
    text(s, "Manuals mix zero-based addresses and one-based register numbers. When values look shifted by one register, check this first.", { x: MX + 8.85, y: TOP + 1.3, w: CW - 9.1, h: 2.1, fontSize: 16 });
    text(s, "Multi-register values start at dataSource and take the next registers: 2 for 32-bit, 4 for 64-bit, length for strings. Hex offsets are accepted: hr:0x10.", { x: MX, y: TOP + 3.95, w: CW, h: 0.9, fontSize: 16 });
    s.addNotes("The data source of a data point is the Modbus address. Prefixed forms are zero-based protocol addresses; five or six digit Modicon numbers are one-based and carry the table in the first digit. Plain numbers without a table are rejected on purpose, to avoid off-by-one ambiguity. hr colon zero and 40001 are the same register.");
  }
  // 11. Types and order
  {
    const s = contentSlide("Modbus", "Modbus: data types and byte order");
    table(s, ["dataType", "Registers", "JSON"], [
      ["Boolean", "bit or 1 (with bit)", "boolean"],
      ["Int16 · UInt16", "1", "integer"],
      ["Int32 · UInt32", "2", "integer"],
      ["Int64 · UInt64", "4", "integer"],
      ["Float32", "2", "number"],
      ["Float64", "4", "number"],
      ["String", "length", "string"],
    ], MX, TOP + 0.05, 6.0, [2.3, 2.2, 1.5], { monoCols: [0], rowH: 0.46, name: "data type table" });
    table(s, ["Device sends", "byteOrder", "wordOrder"], [
      ["AB CD", "BigEndian", "BigEndian"],
      ["CD AB  (word swap)", "BigEndian", "LittleEndian"],
      ["BA DC  (byte swap)", "LittleEndian", "BigEndian"],
      ["DC BA", "LittleEndian", "LittleEndian"],
    ], MX + 6.5, TOP + 0.05, CW - 6.5, [2.45, 1.7, 1.75], { monoCols: [0], rowH: 0.46, name: "byte order table" });
    card(s, MX + 6.5, TOP + 2.6, CW - 6.5, 1.65, "float tip card");
    text(s, [
      { text: "Float looks absurd (1.4E-41)?", options: { bold: true, breakLine: true } },
      { text: "Try wordOrder: LittleEndian first; CDAB is the most common layout in PLCs and meters." },
    ], { x: MX + 6.75, y: TOP + 2.8, w: CW - 7.0, h: 1.3, fontSize: 16 });
    text(s, "scale and offset turn any numeric value into raw × scale + offset. NaN and infinity are published as null.", { x: MX, y: TOP + 3.95, w: 6.0, h: 0.9, fontSize: 14, color: C.accent4 });
    s.addNotes("Ten data types cover practically every device: booleans, sixteen, thirty-two and sixty-four bit integers, IEEE floats, and ASCII strings. Byte order and word order are independent; the four combinations are the familiar ABCD, CDAB, BADC and DCBA. If a float looks absurd, the word order is almost always the culprit.");
  }
  // 12. Register to payload
  {
    const s = contentSlide("Modbus", "Modbus: from registers to payload");
    text(s, "Asset dataset (data points)", { x: MX, y: TOP, w: 6, h: 0.4, fontSize: 16, bold: true, color: C.accent4 });
    code(s, [
      "- name: temperature",
      "  dataSource: hr:0",
      "  dataPointConfiguration:",
      "    '{\"dataType\": \"Float32\"}'",
      "- name: humidity",
      "  dataSource: hr:2",
      "  dataPointConfiguration: '{\"scale\": 0.1}'",
      "- name: running",
      "  dataSource: hr:6",
      "  dataPointConfiguration: '{\"bit\": 0}'",
      "- name: pumpOn",
      "  dataSource: coil:0",
    ], MX, TOP + 0.45, 6.3, 4.45, { name: "yaml block" });
    arrow(s, MX + 6.45, TOP + 2.65, MX + 7.25, TOP + 2.65, C.accent1, "to payload");
    text(s, "Published every samplingIntervalMs", { x: MX + 7.4, y: TOP, w: 5, h: 0.4, fontSize: 16, bold: true, color: C.accent4 });
    code(s, [
      "{",
      "  \"temperature\": 25.53,",
      "  \"humidity\": 50.7,",
      "  \"running\": true,",
      "  \"pumpOn\": true",
      "}",
    ], MX + 7.4, TOP + 0.45, CW - 7.4, 2.6, { dark: true, name: "json block", fontSize: 16 });
    text(s, [
      { text: "3 requests: ", options: { bold: true } },
      { text: "hr:0-2 merged, hr:6 separate (gap), coil:0. A JSON Schema with these four properties is registered for the dataset." },
    ], { x: MX + 7.4, y: TOP + 3.3, w: CW - 7.4, h: 1.5, fontSize: 16 });
    s.addNotes("On the left, four data points as they appear in the asset manifest; the portal form produces the same JSON. On the right, the payload published on every sampling interval: a flat object, property order follows the data points. The planner merges registers 0 to 2 into one request; register 6 is a separate request because gap bridging is off; the coil is its own request.");
  }
  // 14. Five steps
  {
    const s = contentSlide("Modbus", "Modbus: five steps to a running connector");
    const steps = [
      ["Publish image", "aio-modbus-connector, built by the .NET SDK, no Docker"],
      ["Push metadata", "oras push with the Akri connector media type"],
      ["Connector template", "Azure portal or az iot ops connector template create"],
      ["Device", "inbound endpoint Custom.ModbusTcp, address and unit id"],
      ["Asset", "datasets with destinations, one data point per register"],
    ];
    const gap = 0.3, cw = (CW - 4 * gap) / 5, y = TOP + 0.4;
    s.addShape(pres.shapes.LINE, { x: MX + cw / 2, y: y + 0.45, w: CW - cw, h: 0, line: { color: C.accent4, width: 2 }, objectName: "timeline line" });
    steps.forEach(([head, body], i) => {
      const x = MX + i * (cw + gap);
      s.addShape(pres.shapes.OVAL, { x: x + cw / 2 - 0.45, y, w: 0.9, h: 0.9, fill: { color: i < 2 ? C.accent1 : C.accent2 }, line: { type: "none" }, objectName: `step ${i + 1} circle` });
      text(s, String(i + 1), { x: x + cw / 2 - 0.45, y, w: 0.9, h: 0.9, fontSize: 28, bold: true, align: "center", valign: "middle", color: i < 2 ? C.text1 : C.background1 });
      text(s, head, { x, y: y + 1.15, w: cw, h: 0.45, fontSize: 18, bold: true, align: "center" });
      text(s, body, { x: x + 0.05, y: y + 1.65, w: cw - 0.1, h: 1.3, fontSize: 15, color: C.accent4, align: "center" });
    });
    card(s, MX, 5.05, CW, 1.15, "outcome card", C.background2);
    await iconCircle(s, "FaCheckCircle", MX + 0.25, 5.2, 0.85, C.accent5, HEX.lt1, "outcome");
    text(s, [
      { text: "Then it is automatic: ", options: { bold: true } },
      { text: "the Akri operator starts a pod named after the template; it samples the asset and reports status on the device." },
    ], { x: MX + 1.35, y: 5.2, w: CW - 1.6, h: 0.85, fontSize: 16, valign: "middle" });
    s.addNotes("Steps one and two put two things in the container registry: the image, built by the .NET SDK without Docker, and the metadata artifact, pushed with ORAS using the Akri connector media type. Step three creates the connector template on the instance, from the portal or the CLI. Steps four and five are the operator's work: a device with a Custom.ModbusTcp endpoint and an asset with registers. The S7 mode follows the same five steps with its own image and template. The pod starts by itself.");
  }
  // 15. Metadata drives the portal
  {
    const s = contentSlide("Modbus", "Modbus: metadata drives the portal forms");
    text(s, "connector-metadata.json (excerpt)", { x: MX, y: TOP, w: 6, h: 0.4, fontSize: 16, bold: true, color: C.accent4 });
    code(s, [
      "\"dataPointConfigurationSchema\": {",
      "  \"properties\": {",
      "    \"dataType\": { \"title\": \"Data type\",",
      "      \"enum\": [\"Boolean\", \"Int16\", ...] },",
      "    \"wordOrder\": { \"title\": \"Word order\",",
      "      \"enum\": [\"BigEndian\", \"LittleEndian\"] },",
      "    \"scale\": { \"title\": \"Scale\",",
      "      \"type\": \"number\" },",
      "    \"bit\": { \"minimum\": 0, \"maximum\": 15 }",
      "  } }",
    ], MX, TOP + 0.45, 6.3, 4.0, { name: "schema block", fontSize: 14 });
    arrow(s, MX + 6.45, TOP + 2.45, MX + 7.25, TOP + 2.45, C.accent1, "schema to form");
    const fx = MX + 7.4, fw = CW - 7.4;
    text(s, "Register form (illustration)", { x: fx, y: TOP, w: fw, h: 0.4, fontSize: 16, bold: true, color: C.accent4 });
    card(s, fx, TOP + 0.45, fw, 4.0, "form mock card", C.background1);
    const fields = [["Data source", "hr:0"], ["Data type", "Float32  ▾"], ["Word order", "LittleEndian  ▾"], ["Scale", "0.1"], ["Unit id", "1"]];
    fields.forEach(([label, value], i) => {
      const y = TOP + 0.7 + i * 0.73;
      text(s, label, { x: fx + 0.3, y: y + 0.08, w: 1.8, h: 0.4, fontSize: 15, bold: true });
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: fx + 2.15, y, w: fw - 2.5, h: 0.52, rectRadius: 0.05, fill: { color: C.background2 }, line: { color: "C9D3D7", width: 0.75 }, objectName: `${label} field` });
      text(s, value, { x: fx + 2.3, y: y + 0.08, w: fw - 2.8, h: 0.38, fontSize: 15, fontFace: MONO });
    });
    text(s, "The same schemas exist for the endpoint (Advanced tab) and for datasets; data points are renamed \"registers\" in the UI.", { x: MX, y: TOP + 4.65, w: CW, h: 0.6, fontSize: 14, italic: true, color: C.accent4 });
    s.addNotes("This is what makes the portal Modbus-aware. The metadata file contains JSON schemas for the endpoint additional configuration, the dataset configuration and the data point configuration. The operations experience renders them as form fields, so operators pick a data type from a list instead of typing JSON. The form on the right is an illustration, not a screenshot: the portal flow has not been run with this connector yet.");
  }
  // 16. CLI
  {
    const s = contentSlide("Modbus", "Modbus: deploying with the Azure CLI");
    code(s, [
      "./deploy/Publish-Connector.ps1 -Protocol modbus -AcrName myacr `",
      "    -ResourceGroup rg-aio -Instance aio-instance -IncludeSimulator",
      "",
      "az iot ops ns device endpoint inbound add custom --device line1-plc `",
      "    --name plc-modbus --endpoint-type Custom.ModbusTcp --version 1.0 `",
      "    --endpoint-address modbus-tcp://192.168.1.10:502 -i aio-instance -g rg-aio",
      "",
      "az iot ops ns asset custom datapoint add --asset line1-oven --dataset process `",
      "    --name temperature --data-source hr:0 --config '{\"dataType\":\"Float32\"}' `",
      "    -i aio-instance -g rg-aio",
    ], MX, TOP, CW, 3.55, { dark: true, name: "cli block", fontSize: 14 });
    const tiles = [
      ["FaRocket", "Publish-Connector.ps1", "image, metadata, connector template"],
      ["FaClipboardList", "modbus/New-SampleAsset.ps1", "device, endpoint, asset, 14 registers"],
      ["FaRobot", "AGENT.md", "guide for automated agents, both modes"],
    ];
    const tw = (CW - 0.6) / 3;
    for (let i = 0; i < 3; i++) {
      const [icon, head, body] = tiles[i];
      const x = MX + i * (tw + 0.3), y = TOP + 3.85;
      card(s, x, y, tw, 1.25, `${head} tile`);
      await iconCircle(s, icon, x + 0.2, y + 0.22, 0.8, i === 2 ? C.accent2 : C.accent1, i === 2 ? HEX.lt1 : HEX.dk2, head);
      text(s, head, { x: x + 1.15, y: y + 0.2, w: tw - 1.3, h: 0.4, fontSize: 16, bold: true });
      text(s, body, { x: x + 1.15, y: y + 0.62, w: tw - 1.3, h: 0.5, fontSize: 14, color: C.accent4 });
    }
    s.addNotes("Everything the portal does is also scriptable. Publish-Connector does the image, the metadata and the template. New-SampleAsset creates the sample device and an asset with fourteen registers through Azure Resource Manager, so they are visible in the portal. AGENT.md is written for an automated agent: inputs, preflight checks, commands, verification and guardrails.");
  }
  // 18. Three ways
  {
    const s = contentSlide("Modbus", "Modbus: three ways to run locally");
    const cols = [
      ["FaVial", "Unit tests", "In-process NModbus simulator on a free loopback port, full sampling chain", ["dotnet test --solution", "  AIOIndustrialConnector.slnx"]],
      ["FaTerminal", "Probe + simulator", "Deploy manifests sampled in one process; prints each payload and schema", ["dotnet run --project", "  src/Tools/IndustrialProbe", "  -- --protocol modbus", "  --device device.yaml", "  --asset asset.yaml --simulator"]],
      ["FaServer", "Standalone simulator", "Real TCP server on port 5020; probe or any Modbus tool connects", ["dotnet run --project", "  src/Modbus/ModbusSimulator", "  -- --port 5020"]],
    ];
    const cw = (CW - 0.8) / 3, h = 4.85;
    for (let i = 0; i < 3; i++) {
      const [icon, head, body, cmd] = cols[i];
      const x = MX + i * (cw + 0.4), y = TOP;
      card(s, x, y, cw, h, `${head} card`);
      await iconCircle(s, icon, x + 0.3, y + 0.3, 0.85, [C.accent5, C.accent1, C.accent2][i], i === 1 ? HEX.dk2 : HEX.lt1, head);
      text(s, head, { x: x + 1.35, y: y + 0.45, w: cw - 1.5, h: 0.5, fontSize: 20, bold: true });
      text(s, body, { x: x + 0.3, y: y + 1.35, w: cw - 0.6, h: 1.2, fontSize: 16 });
      code(s, cmd, x + 0.3, y + 2.75, cw - 0.6, 1.8, { dark: true, fontSize: 13, name: `${head} command` });
    }
    s.addNotes("Unit tests start the simulator inside the test process and exercise the whole sampling chain over real TCP. The probe loads the very manifests you deploy and runs the production sampler against an in-process simulator, printing each payload and schema. The standalone simulator is a real Modbus TCP server on port 5020, so any Modbus tool can talk to it too. The probe is also a safe, read-only way to validate a register map against a real PLC before deploying.");
  }
  // 19. Simulator map + probe output
  {
    const s = contentSlide("Modbus", "Modbus: the simulator and the probe");
    table(s, ["Address", "Value", "Encoding"], [
      ["hr:0-1", "temperature", "Float32 ABCD"],
      ["hr:2", "humidity", "UInt16 × 0.1"],
      ["hr:3-4", "cycleCounter", "UInt32"],
      ["hr:6", "status bits", "bit 0 run, bit 1 alarm"],
      ["hr:10-17", "serialNumber", "String, 8 registers"],
      ["ir:0-1", "flowRate", "Float32 CDAB"],
      ["ir:3", "1234", "byte swapped"],
      ["coil:0-1", "pump, valve", "bits"],
      ["di:0-1", "door, emergency stop", "bits"],
    ], MX, TOP, 6.6, [1.9, 2.3, 2.4], { monoCols: [0], rowH: 0.47, fontSize: 14, name: "register map table" });
    code(s, [
      "$ IndustrialProbe --protocol modbus",
      "    --device device.yaml --asset asset.yaml",
      "    --simulator",
      "",
      "process: 12 point(s) in 4 request(s)",
      "process -> mqtt:modbus/.../process",
      "  {\"temperature\":25.53,",
      "   \"humidity\":50.7,",
      "   \"cycleCounter\":3,",
      "   \"running\":true, ...}",
      "identity -> state-store:...",
      "  {\"serialNumber\":\"AIO-SIM-0001\"}",
    ], MX + 7.0, TOP, CW - 7.0, 4.7, { dark: true, fontSize: 14, name: "probe output" });
    text(s, "The map deliberately includes word-swapped and byte-swapped values, to test decoding options.", { x: MX, y: TOP + 4.85, w: CW, h: 0.5, fontSize: 14, italic: true, color: C.accent4 });
    s.addNotes("The simulator serves a small process cell: a sine temperature, humidity, a counter, a status word with bits, a serial number string, an energy total, a word-swapped flow rate and a byte-swapped value, plus coils and discrete inputs. On the right, real output from the probe run with the deploy manifests: twelve points read in four merged requests.");
  }
  // S1. Section Part 2 S7
  await sectionSlide("S7", "Part 2 · Siemens S7", "PLC preparation, absolute addresses, deployment and simulation", "FaMicrochip").then((s) =>
    s.addNotes("Part two: the Siemens S7 mode. It reuses the whole pipeline of the shared architecture; what is new is the address notation, the decoding, the S7 client and the PLC preparation."));

  // S2. Prepare the PLC
  {
    const s = contentSlide("S7", "S7: prepare the PLC first");
    const items = [
      ["FaPlug", C.accent2, "Permit PUT/GET", "S7-1200/1500: CPU properties, Protection & Security, Connection mechanisms: allow PUT/GET from a remote partner."],
      ["FaDatabase", C.accent3, "Non-optimized data blocks", "Untick Optimized block access on every DB you read. Optimized DBs have no fixed byte offsets, so absolute addressing cannot work."],
    ];
    const cw = (CW - 0.4) / 2;
    for (let i = 0; i < 2; i++) {
      const [icon, fill, head, body] = items[i];
      const x = MX + i * (cw + 0.4);
      card(s, x, TOP, cw, 2.15, `${head} card`);
      await iconCircle(s, icon, x + 0.3, TOP + 0.35, 0.9, fill, HEX.lt1, head);
      text(s, head, { x: x + 1.45, y: TOP + 0.28, w: cw - 1.7, h: 0.45, fontSize: 20, bold: true });
      text(s, body, { x: x + 1.45, y: TOP + 0.8, w: cw - 1.7, h: 1.3, fontSize: 15 });
    }
    table(s, ["PLC", "cpuType", "rack", "slot"], [
      ["S7-1200 / S7-1500", "S71200 / S71500", "0", "1 (0 also works)"],
      ["S7-300", "S7300", "0", "2"],
      ["S7-400", "S7400", "per hardware config", "CPU slot, e.g. 3"],
    ], MX, TOP + 2.5, 8.6, [2.5, 2.2, 1.9, 2.0], { monoCols: [1], rowH: 0.48, fontSize: 14, name: "rack slot table" });
    text(s, "Endpoint address: s7://192.168.0.1:102. The connector only reads; nothing is written to the PLC.", { x: MX + 8.9, y: TOP + 2.55, w: CW - 8.9, h: 1.5, fontSize: 15, color: C.accent4 });
    s.addNotes("Most S7 problems are PLC settings, not connector settings. On S7-1200 and S7-1500 the CPU must permit PUT/GET access, and every data block you read must be non-optimized, otherwise the PLC answers with an error. Rack and slot depend on the CPU family. This is verified only against the simulator so far, not on real hardware.");
  }

  // S3. Addressing
  {
    const s = contentSlide("S7", "S7: addressing a tag");
    table(s, ["dataSource", "Area", "Width"], [
      ["DB1.DBX12.0", "data block 1", "bit 0 of byte 12"],
      ["DB1.DBB20", "data block 1", "byte (start of strings, 64-bit)"],
      ["DB1.DBW4", "data block 1", "word, bytes 4-5"],
      ["DB1.DBD0", "data block 1", "double word, bytes 0-3"],
      ["M0.1 · MB5 · MW10 · MD20", "memory (flags)", "bit · byte · word · dword"],
      ["I0.0 · IW2 · E1.7", "inputs", "German E accepted"],
      ["Q0.1 · QD4 · A0.0", "outputs", "German A accepted"],
    ], MX, TOP + 0.05, 8.4, [3.2, 2.1, 3.1], { monoCols: [0], rowH: 0.5, fontSize: 14, name: "s7 address table" });
    card(s, MX + 8.8, TOP + 0.05, CW - 8.8, 3.9, "byte offsets card");
    await iconCircle(s, "FaExclamationTriangle", MX + 9.05, TOP + 0.3, 0.8, C.accent3, HEX.lt1, "warning");
    text(s, "DBW4 = 4..5", { x: MX + 10.05, y: TOP + 0.42, w: 2.2, h: 0.5, fontSize: 18, bold: true, fontFace: MONO });
    text(s, "Offsets are zero-based byte offsets, as in the PLC program, not word numbers. DBW4 covers bytes 4 and 5. Counters and timers are not supported.", { x: MX + 9.05, y: TOP + 1.3, w: CW - 9.3, h: 2.5, fontSize: 15 });
    text(s, "Siemens notation exactly as TIA Portal shows it, case-insensitive. Bad addresses are reported with the valid forms.", { x: MX, y: TOP + 4.25, w: CW, h: 0.6, fontSize: 14, italic: true, color: C.accent4 });
    s.addNotes("The data source of a tag is the Siemens absolute address, as shown in TIA Portal. Data block addresses carry the block number; memory, input and output areas use M, I and Q, and the German E and A are accepted. Offsets are byte offsets, which is the usual source of off-by-one mistakes. Unlike Modbus there is no one-based variant.");
  }

  // S4. Types and width
  {
    const s = contentSlide("S7", "S7: types must fit the address width");
    table(s, ["Address", "Default type", "Also allowed"], [
      ["DBX / M0.1 (bit)", "Boolean", "-"],
      ["DBB (byte)", "Byte", "any: also the start of Float64, Int64, String"],
      ["DBW (word)", "UInt16", "Int16, Boolean with bit"],
      ["DBD (double word)", "UInt32", "Int32, Float32, Boolean with bit"],
    ], MX, TOP + 0.05, CW, [3.2, 2.4, 6.5], { monoCols: [0], rowH: 0.4, fontSize: 13, name: "width table" });
    table(s, ["Siemens", "dataType", "Siemens", "dataType"], [
      ["BOOL", "Boolean", "DINT", "Int32"],
      ["BYTE · USINT", "Byte", "DWORD · UDINT", "UInt32"],
      ["SINT", "SByte", "LINT · ULINT", "Int64 · UInt64"],
      ["INT", "Int16", "REAL", "Float32"],
      ["WORD · UINT", "UInt16", "LREAL", "Float64"],
      ["STRING[n]", "String + length n", "", ""],
    ], MX, TOP + 2.3, 8.3, [2.2, 2.0, 2.2, 1.9], { monoCols: [1, 3], rowH: 0.36, fontSize: 12, name: "siemens type table" });
    card(s, MX + 8.7, TOP + 2.3, CW - 8.7, 2.7, "mismatch card");
    text(s, [
      { text: "A mismatch is an error, not a reinterpretation.", options: { bold: true, breakLine: true } },
      { text: "Float32 on DBW is rejected with a message naming the valid combinations. Both Siemens and common names are accepted in dataType. S7 is always big endian: no byte or word order." },
    ], { x: MX + 8.95, y: TOP + 2.5, w: CW - 9.2, h: 2.3, fontSize: 14 });
    s.addNotes("The address width fixes the default type, and a type that does not fit is a configuration error, reported together with all other errors. The B form doubles as the untyped start address for sixty-four bit values and strings. Siemens type names work in the data type setting, so Real, DInt and Word are as valid as Float32, Int32 and UInt16. S7 strings are read with their two header bytes and published with their current length.");
  }

  // S5. Payload
  {
    const s = contentSlide("S7", "S7: from tags to payload");
    text(s, "Asset dataset (data points)", { x: MX, y: TOP, w: 6, h: 0.4, fontSize: 16, bold: true, color: C.accent4 });
    code(s, [
      "- name: temperature",
      "  dataSource: DB1.DBD0",
      "  dataPointConfiguration:",
      "    '{\"dataType\": \"Real\"}'",
      "- name: humidity",
      "  dataSource: DB1.DBW4",
      "  dataPointConfiguration:",
      "    '{\"dataType\": \"Int\", \"scale\": 0.1}'",
      "- name: running",
      "  dataSource: DB1.DBX12.0",
      "- name: pumpOn",
      "  dataSource: Q0.0",
    ], MX, TOP + 0.45, 6.3, 4.45, { name: "s7 yaml block" });
    arrow(s, MX + 6.45, TOP + 2.65, MX + 7.25, TOP + 2.65, C.accent1, "to payload");
    text(s, "Published every samplingIntervalMs", { x: MX + 7.4, y: TOP, w: 5, h: 0.4, fontSize: 16, bold: true, color: C.accent4 });
    code(s, [
      "{",
      "  \"temperature\": 25.0,",
      "  \"humidity\": 50.0,",
      "  \"running\": true,",
      "  \"pumpOn\": true",
      "}",
    ], MX + 7.4, TOP + 0.45, CW - 7.4, 2.6, { dark: true, name: "s7 json block", fontSize: 16 });
    text(s, [
      { text: "2 requests: ", options: { bold: true } },
      { text: "DB1 bytes 0-12 merged in one read, Q0.0 on its own. The full sample map reads 11 points in 4 requests. Same payload shape and JSON Schema as Modbus." },
    ], { x: MX + 7.4, y: TOP + 3.3, w: CW - 7.4, h: 1.5, fontSize: 16 });
    s.addNotes("The same idea as in the Modbus part. Four tags on the left, a flat JSON object on the right. The planner merges everything in data block one into a single read and reads the output area separately. The full sample asset in the repository reads eleven points in four requests. The payload shape and the registered JSON Schema are identical to the Modbus mode, because that part is shared code.");
  }

  // S6. Deploy S7
  {
    const s = contentSlide("S7", "S7: deploying the S7 mode");
    code(s, [
      "./deploy/Publish-Connector.ps1 -Protocol s7 -AcrName myacr `",
      "    -ResourceGroup rg-aio -Instance aio-instance -IncludeSimulator",
      "",
      "az iot ops ns device endpoint inbound add custom --device line1-plc `",
      "    --name plc-s7 --endpoint-type Custom.SiemensS7 --version 1.0 `",
      "    --endpoint-address s7://192.168.0.1:102 `",
      "    --additional-config '{\"cpuType\":\"S71500\",\"rack\":0,\"slot\":1}' `",
      "    -i aio-instance -g rg-aio",
    ], MX, TOP, CW, 3.0, { dark: true, name: "s7 cli block", fontSize: 14 });
    const tiles = [
      ["FaBox", "aio-s7-connector", "image with the S7 mode baked in"],
      ["FaFileCode", "s7 metadata", "portal forms: CPU type, rack, slot, tags"],
      ["FaClipboardList", "s7/New-SampleAsset.ps1", "device, endpoint, asset, 13 tags"],
    ];
    const tw = (CW - 0.6) / 3;
    for (let i = 0; i < 3; i++) {
      const [icon, head, body] = tiles[i];
      const x = MX + i * (tw + 0.3), y = TOP + 3.3;
      card(s, x, y, tw, 1.25, `${head} tile`);
      await iconCircle(s, icon, x + 0.2, y + 0.22, 0.8, i === 1 ? C.accent2 : C.accent1, i === 1 ? HEX.lt1 : HEX.dk2, head);
      text(s, head, { x: x + 1.15, y: y + 0.2, w: tw - 1.3, h: 0.4, fontSize: 16, bold: true });
      text(s, body, { x: x + 1.15, y: y + 0.62, w: tw - 1.3, h: 0.5, fontSize: 14, color: C.accent4 });
    }
    text(s, "Same five steps as Modbus: publish image, push metadata, create template, device, asset. The portal calls data points tags.", { x: MX, y: TOP + 4.8, w: CW, h: 0.5, fontSize: 14, italic: true, color: C.accent4 });
    s.addNotes("Deployment is the same five steps as for Modbus, with the protocol parameter set to s7. The image is aio-s7-connector, with the mode baked in, the metadata artifact has its own portal schemas for CPU type, rack, slot and tags, and the template is s7-connector. The portal flow has not been clicked through with this connector yet.");
  }

  // S7. Local S7
  {
    const s = contentSlide("S7", "S7: simulator and probe");
    table(s, ["Address", "Value", "Encoding"], [
      ["DB1.DBD0", "temperature", "REAL"],
      ["DB1.DBW4", "humidity", "INT × 0.1"],
      ["DB1.DBD6", "cycleCounter", "DWORD"],
      ["DB1.DBX12.0/1", "running, alarm", "BOOL"],
      ["DB1.DBB20", "serialNumber", "STRING[16]"],
      ["DB1.DBB40", "totalEnergy", "LREAL"],
      ["MW0", "tank level raw", "WORD"],
      ["I0.0/1 · Q0.0/1", "door, e-stop · pump, valve", "BOOL"],
    ], MX, TOP, 6.6, [2.2, 2.4, 2.0], { monoCols: [0], rowH: 0.47, fontSize: 14, name: "s7 memory map table" });
    code(s, [
      "$ IndustrialProbe --protocol s7",
      "    --device device.yaml",
      "    --asset asset.yaml --simulator",
      "",
      "Connected to S7 PLC 127.0.0.1:10200",
      "process: 11 point(s) in 4 request(s)",
      "process -> mqtt:s7/.../process",
      "  {\"temperature\":25.0005,",
      "   \"humidity\":50,",
      "   \"running\":true,",
      "   \"pumpOn\":true, ...}",
    ], MX + 7.0, TOP, CW - 7.0, 4.2, { dark: true, fontSize: 14, name: "s7 probe output" });
    text(s, "The S7 simulator is an independent implementation of ISO-on-TCP and S7comm read, written from the wire format. S7netplus talks to it over real TCP: same tests as Modbus, plus PDU splitting.", { x: MX, y: TOP + 4.5, w: CW, h: 0.8, fontSize: 14, italic: true, color: C.accent4 });
    s.addNotes("The S7 simulator serves the same process cell as the Modbus one, in S7 notation. It is not a wrapper around the client library: it implements the TPKT, COTP and S7comm read services itself, so the tests exercise a real protocol round trip, including a read larger than one PDU that the client splits. On the right is real output from the probe with the sample manifests. This proves the code path, not compatibility with every real CPU.");
  }
  // 20. Section: Status
  await sectionSlide("Status", "Where it stands", "What is verified, what is not, what comes next", "FaClipboardCheck").then((s) =>
    s.addNotes("Finally, the honest status."));
  // 21. Verified locally
  {
    const s = contentSlide("Status", "Verified locally");
    s.addChart(pres.charts.BAR, [{ name: "Tests", labels: ["Core (shared)", "Modbus", "S7"], values: [14, 72, 73] }], {
      x: MX, y: TOP, w: 6.6, h: 4.6, barDir: "bar", objectName: "tests by area chart",
      showTitle: true, title: "159 tests by project, all passing", titleFontSize: 16, titleColor: HEX.dk1, titleFontFace: "+mn-lt",
      showValue: true, dataLabelPosition: "outEnd", dataLabelFontSize: 14, dataLabelColor: HEX.dk1, dataLabelFontFace: "+mn-lt",
      chartColors: [HEX.accent5], catAxisLabelColor: HEX.dk1, catAxisLabelFontSize: 14, catAxisLabelFontFace: "+mn-lt",
      valAxisHidden: true, valGridLine: { style: "none" }, catGridLine: { style: "none" }, showLegend: false, catAxisOrientation: "maxMin",
    });
    const checks = [
      "Build of all projects, no warnings",
      "Probe runs of both manifest sets against their simulators",
      "S7netplus client against the independent S7 simulator",
      "S7 container image built, mode baked in",
      "Modbus metadata schema-validated in 0.1.0 (S7 not yet)",
    ];
    for (let i = 0; i < checks.length; i++) {
      const y = TOP + 0.2 + i * 0.88;
      await iconCircle(s, "FaCheck", MX + 7.1, y, 0.55, C.accent5, HEX.lt1, `check ${i + 1}`);
      text(s, checks[i], { x: MX + 7.85, y: y + 0.05, w: CW - 7.85, h: 0.55, fontSize: 16, valign: "middle" });
    }
    s.addNotes("Locally, the evidence is solid: one hundred fifty-nine tests in three projects, all passing, with the Modbus tests unchanged in behaviour after the refactoring; probe runs of both manifest sets against their simulators; the S7 client talking to an independent S7 simulator over real TCP; and the S7 container image built with the mode baked in. The Modbus metadata was validated against Microsoft's schema in version 0.1; the S7 metadata still has to be.");
  }
  // 22. Not yet verified
  {
    const s = contentSlide("Status", "Not yet verified, and known limits");
    const left = [
      "No run on a live AIO cluster (Akri, MQTT, ADR status)",
      "S7 never tried on a real PLC, only on a simulator",
      "S7 metadata not schema-validated; portal flows not clicked",
      "Scripts and the refactored host wiring not run in Azure",
    ];
    const right = [
      "Read only: no writes in either protocol",
      "Classic S7comm: PUT/GET and non-optimized DBs required",
      "Modbus TCP only: no RTU over TCP, no TLS",
      "Polling only; not runnable outside AIO yet",
    ];
    const cw = (CW - 0.4) / 2;
    const cols = [[left, "Not yet verified", "FaExclamationTriangle", C.accent3], [right, "Limits of 0.2.0", "FaInfoCircle", C.accent4]];
    for (let c = 0; c < 2; c++) {
      const [items, head, icon, fill] = cols[c];
      const x = MX + c * (cw + 0.4);
      card(s, x, TOP, cw, 3.7, `${head} card`);
      await iconCircle(s, icon, x + 0.3, TOP + 0.3, 0.8, fill, HEX.lt1, head);
      text(s, head, { x: x + 1.3, y: TOP + 0.42, w: cw - 1.5, h: 0.5, fontSize: 20, bold: true });
      text(s, bullets(items, 16), { x: x + 0.35, y: TOP + 1.35, w: cw - 0.7, h: 3.0 });
    }
    s.addNotes("What is not verified: nothing has run on a live cluster yet, so publishing, status reporting and the Akri deployment are untested, including the host wiring changed by the refactoring; S7 was never tried on a real PLC; the portal steps follow Microsoft's documentation but were not clicked through; the scripts were not run against Azure. Known limits of version 0.2: read only, classic S7 communication only, Modbus TCP only, polling only, and the host cannot run outside AIO yet. All of this is tracked in OPENPOINTS.md.");
  }
  // 23. Roadmap
  {
    const s = contentSlide("Status", "Roadmap");
    const stages = [
      ["0.2.0", "now", ["Unified core", "Modbus + S7 modes", "Docs per protocol"], C.accent5],
      ["Next", "", ["Run outside AIO", "Live AIO + real PLC", "CI and arm64 images"], C.accent1],
      ["Then", "", ["Writes via management", "Report by exception", "Value rounding"], C.accent2],
      ["Later", "", ["S7 timers, WSTRING", "Modbus RTU, TLS", "Discovery, new protocols"], C.accent4],
    ];
    const gap = 0.35, cw = (CW - 3 * gap) / 4, y = TOP + 0.2;
    s.addShape(pres.shapes.LINE, { x: MX + 0.3, y: y + 0.4, w: CW - 0.6, h: 0, line: { color: C.accent4, width: 2, endArrowType: "triangle" }, objectName: "roadmap line" });
    stages.forEach(([ver, tag, items, fill], i) => {
      const x = MX + i * (cw + gap);
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: x + 0.2, y, w: 1.5, h: 0.8, rectRadius: 0.1, fill: { color: fill }, line: { type: "none" }, objectName: `${ver} badge` });
      text(s, ver, { x: x + 0.2, y, w: 1.5, h: 0.8, fontSize: 20, bold: true, align: "center", valign: "middle", color: i === 1 ? C.text1 : C.background1 });
      if (tag) text(s, tag, { x: x + 1.85, y: y + 0.2, w: 1, h: 0.4, fontSize: 14, italic: true, color: C.accent4 });
      card(s, x, y + 1.15, cw, 2.4, `${ver} card`);
      text(s, bullets(items, 16), { x: x + 0.25, y: y + 1.4, w: cw - 0.5, h: 2.0 });
    });
    s.addNotes("Version 0.2 is this unified architecture with both protocols. Next: standalone execution outside AIO, which was requested for flexible installation and testing, validation on a live instance and on real PLCs, and CI with arm64 images. Then writes through management actions and publishing on change. Later: S7 timers and wide strings, Modbus over RTU framing and TLS, discovery, and further protocols as new modules on the same core.");
  }
  // 24. Closing
  pres.addSection({ title: "Close" });
  {
    const s = pres.addSlide({ masterName: "TITLE_DARK", sectionTitle: "Close" });
    s.addText("Next: live AIO, real PLCs, bare metal", { placeholder: "title" });
    s.addText("One repository: core, two protocol modules, simulators, scripts and guides", { placeholder: "body" });
    const docs = [["README · USAGE", "start here"], ["docs/modbus", "Modbus guides"], ["docs/s7", "S7 guides"], ["ARCHITECTURE", "core + modules"], ["AGENT.md", "automated deploy"]];
    const cw = (CW - 4 * 0.25) / 5;
    docs.forEach(([name, what], i) => {
      const x = MX + i * (cw + 0.25), y = 5.2;
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w: cw, h: 1.1, rectRadius: 0.08, fill: { color: C.accent6 }, line: { type: "none" }, objectName: `${name} tile` });
      text(s, [{ text: name, options: { bold: true, breakLine: true, fontSize: 15 } }, { text: what, options: { fontSize: 14, color: C.accent1 } }],
        { x: x + 0.15, y: y + 0.12, w: cw - 0.3, h: 0.86, color: C.background1, valign: "middle" });
    });
    s.addNotes("The next milestone is a run on a live Azure IoT Operations instance, following the PORTAL guide of each protocol or AGENT.md, and to start the standalone mode once the configuration approach is decided. Everything needed is in the repository.");
  }

  await pres.writeFile({ fileName: OUT });
  if (APPLY_THEME) {
    const { applyTheme } = require(APPLY_THEME);
    await applyTheme(OUT, THEME);
  }
  console.log("Wrote " + OUT);
}

build().catch((e) => { console.error(e); process.exit(1); });
