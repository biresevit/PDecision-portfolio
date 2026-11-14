"""Builds the PDecision client API documentation PDF."""
import os
import sys
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_LEFT, TA_CENTER
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, PageTemplate, Frame, Paragraph, Spacer, Table,
                                TableStyle, PageBreak, Image, XPreformatted, KeepTogether, NextPageTemplate)
from reportlab.platypus.tableofcontents import TableOfContents

OUT = sys.argv[1]
PROFILE_IMG = sys.argv[2]

F = "C:/Windows/Fonts/"
pdfmetrics.registerFont(TTFont("Body", F + "segoeui.ttf"))
pdfmetrics.registerFont(TTFont("Body-Bold", F + "segoeuib.ttf"))
pdfmetrics.registerFont(TTFont("Body-Italic", F + "segoeuii.ttf"))
pdfmetrics.registerFont(TTFont("Body-Light", F + "segoeuil.ttf"))
pdfmetrics.registerFont(TTFont("Mono", F + "consola.ttf"))
pdfmetrics.registerFont(TTFont("Mono-Bold", F + "consolab.ttf"))
from reportlab.pdfbase.pdfmetrics import registerFontFamily
registerFontFamily("Body", normal="Body", bold="Body-Bold", italic="Body-Italic", boldItalic="Body-Bold")
registerFontFamily("Mono", normal="Mono", bold="Mono-Bold", italic="Mono", boldItalic="Mono-Bold")

INK = colors.HexColor("#1d1a24")
MUTED = colors.HexColor("#5f5a6b")
ACCENT = colors.HexColor("#3b0a4f")      # deep purple, matches profile artwork
ACCENT2 = colors.HexColor("#0a8fc9")
RULE = colors.HexColor("#d9d4e2")
CODEBG = colors.HexColor("#f5f3f8")
HEADBG = colors.HexColor("#efeaf5")
WARNBG = colors.HexColor("#fff6e5")
WARNBAR = colors.HexColor("#e39b16")

ss = {}
ss["body"] = ParagraphStyle("body", fontName="Body", fontSize=9.6, leading=14.2, textColor=INK, spaceAfter=6)
ss["small"] = ParagraphStyle("small", parent=ss["body"], fontSize=8.4, leading=11.5, textColor=MUTED)
ss["cell"] = ParagraphStyle("cell", parent=ss["body"], fontSize=8.5, leading=11.6, spaceAfter=0)
ss["cellb"] = ParagraphStyle("cellb", parent=ss["cell"], fontName="Body-Bold")
ss["cellm"] = ParagraphStyle("cellm", parent=ss["cell"], fontName="Mono", fontSize=8.2)
ss["h1"] = ParagraphStyle("h1", fontName="Body-Bold", fontSize=19, leading=24, textColor=ACCENT, spaceBefore=4, spaceAfter=10)
ss["h2"] = ParagraphStyle("h2", fontName="Body-Bold", fontSize=12.5, leading=17, textColor=INK, spaceBefore=12, spaceAfter=5)
ss["h3"] = ParagraphStyle("h3", fontName="Body-Bold", fontSize=10.2, leading=14, textColor=ACCENT, spaceBefore=8, spaceAfter=3)
ss["bullet"] = ParagraphStyle("bullet", parent=ss["body"], leftIndent=12, bulletIndent=2, spaceAfter=3)
ss["code"] = ParagraphStyle("code", fontName="Mono", fontSize=7.9, leading=10.4, textColor=INK)
ss["toc1"] = ParagraphStyle("toc1", fontName="Body", fontSize=10, leading=13, textColor=INK, leftIndent=0)
ss["toc2"] = ParagraphStyle("toc2", fontName="Body", fontSize=8.5, leading=10.2, textColor=MUTED, leftIndent=14)


def P(t, s="body"):
    return Paragraph(t, ss[s])


def bullets(items):
    return [Paragraph(i, ss["bullet"], bulletText="\u2022") for i in items]


def steps(items):
    return [Paragraph(i, ss["bullet"], bulletText=f"{n}.") for n, i in enumerate(items, 1)]


def code(text):
    # code is shown as typed, so escape the characters reportlab reads as markup (e.g. C# generics)
    text = text.strip("\n").replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    x = XPreformatted(text, ss["code"])
    t = Table([[x]], colWidths=[170 * mm])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), CODEBG),
        ("BOX", (0, 0), (-1, -1), 0.5, RULE),
        ("LEFTPADDING", (0, 0), (-1, -1), 8), ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
    ]))
    return KeepTogether([t, Spacer(1, 6)])


def note(text, title="Note"):
    p = Paragraph(f"<b>{title}.</b> {text}", ss["cell"])
    t = Table([[p]], colWidths=[170 * mm])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), WARNBG),
        ("LINEBEFORE", (0, 0), (0, -1), 3, WARNBAR),
        ("LEFTPADDING", (0, 0), (-1, -1), 9), ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
    ]))
    return KeepTogether([Spacer(1, 2), t, Spacer(1, 8)])


def table(rows, widths, mono_cols=(), header=True, keep=True):
    data = []
    for r, row in enumerate(rows):
        out = []
        for c, v in enumerate(row):
            if isinstance(v, str):
                st = "cellb" if (header and r == 0) else ("cellm" if c in mono_cols else "cell")
                out.append(Paragraph(v, ss[st]))
            else:
                out.append(v)
        data.append(out)
    t = Table(data, colWidths=[w * mm for w in widths], repeatRows=1 if header else 0)
    style = [
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LINEBELOW", (0, 0), (-1, -1), 0.4, RULE),
        ("LEFTPADDING", (0, 0), (-1, -1), 5), ("RIGHTPADDING", (0, 0), (-1, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 4), ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ]
    if header:
        style += [("BACKGROUND", (0, 0), (-1, 0), HEADBG), ("LINEBELOW", (0, 0), (-1, 0), 0.8, ACCENT)]
    t.setStyle(TableStyle(style))
    return KeepTogether([t, Spacer(1, 8)]) if keep and len(rows) < 14 else t


class Doc(BaseDocTemplate):
    def __init__(self, fn, **kw):
        super().__init__(fn, pagesize=A4, leftMargin=20 * mm, rightMargin=20 * mm,
                         topMargin=22 * mm, bottomMargin=20 * mm,
                         title="PDecision Strategy Service - Client Integration Guide",
                         author="PDecision", subject="API documentation", **kw)
        fr = Frame(self.leftMargin, self.bottomMargin, self.width, self.height, id="f")
        self.addPageTemplates([PageTemplate("cover", [fr], onPage=self.cover),
                               PageTemplate("main", [fr], onPage=self.chrome)])
        self._h = 0

    def cover(self, c, d):
        w, h = A4
        c.saveState()
        c.setFillColor(ACCENT)
        c.rect(0, h - 120 * mm, w, 120 * mm, stroke=0, fill=1)
        c.setFillColor(ACCENT2)
        c.rect(0, h - 122 * mm, w, 2 * mm, stroke=0, fill=1)
        c.setFillColor(colors.white)
        c.setFont("Body-Light", 13)
        c.drawString(20 * mm, h - 45 * mm, "CLIENT INTEGRATION GUIDE")
        c.setFont("Body-Bold", 34)
        c.drawString(20 * mm, h - 65 * mm, "PDecision")
        c.setFont("Body", 20)
        c.drawString(20 * mm, h - 78 * mm, "Strategy Service API")
        c.setFont("Body-Light", 11)
        c.drawString(20 * mm, h - 98 * mm, "Installation  \u2022  Hand-history sync  \u2022  REST API  \u2022  Player profiles")
        c.setFillColor(MUTED)
        c.setFont("Body", 9.5)
        c.drawString(20 * mm, 30 * mm, "Service version 1.0.0.20   \u2022   Document revision 2.0   \u2022   October 2026")
        c.drawString(20 * mm, 24 * mm, "Confidential \u2013 provided to licensed subscribers only. Do not redistribute.")
        c.restoreState()

    def chrome(self, c, d):
        w, h = A4
        c.saveState()
        c.setStrokeColor(RULE)
        c.setLineWidth(0.5)
        c.line(20 * mm, h - 14 * mm, w - 20 * mm, h - 14 * mm)
        c.setFont("Body", 8)
        c.setFillColor(MUTED)
        c.drawString(20 * mm, h - 12 * mm, "PDecision Strategy Service \u2013 Client Integration Guide")
        c.drawRightString(w - 20 * mm, h - 12 * mm, "Confidential")
        c.line(20 * mm, 14 * mm, w - 20 * mm, 14 * mm)
        c.drawRightString(w - 20 * mm, 9.5 * mm, f"Page {d.page}")
        c.restoreState()

    def afterFlowable(self, f):
        if isinstance(f, Paragraph) and f.style.name in ("h1", "h2"):
            lvl = 0 if f.style.name == "h1" else 1
            text = f.getPlainText()
            key = "h%d" % (abs(hash(text)) % 10**9)
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(text, key, level=lvl, closed=False)
            self.notify("TOCEntry", (lvl, text, self.page, key))


story = [NextPageTemplate("main"), PageBreak()]

# ---------------- Contents ----------------
story.append(P("Contents", "h1x") if False else Paragraph("Contents", ParagraphStyle("ct", parent=ss["h1"])))
toc = TableOfContents()
toc.levelStyles = [ss["toc1"], ss["toc2"]]
toc.dotsMinLevel = 0
story += [toc, PageBreak()]

# ---------------- 1 Overview ----------------
story.append(P("1. Overview", "h1"))
story.append(P(
    "PDecision is a strategy service that runs as a Windows executable on your PC, next to your poker bots. "
    "Your bot reads the table, sends the current table state to PDecision's <b>REST API</b> (JSON over HTTP), and receives back the action to take: "
    "<b>fold, check, call, bet or raise</b>, the <b>bet size</b>, the <b>street</b> the decision belongs to, and the "
    "<b>timing</b> used to make the bot's reaction time look human."))
story.append(P(
    "The service also turns the table states you send into hand histories. Those hand histories are shared between "
    "<b>your own PCs only</b>, so every PC you run keeps the same, fresh statistics on the opponents in the player pool "
    "(see section 3)."))

story.append(P("Supported games", "h2"))
story.append(table([
    ["Game", "Formats", "Preflop", "Postflop"],
    ["No-Limit Hold'em (NLH)", "Cash (ring), SNG, MTT", "Solver-generated preflop charts",
     "Flop at high SPR: precalculated solver solutions. Turn, river and low-SPR flop: solved live on the provider's solver cluster."],
    ["Pot-Limit Omaha 4 (PLO4)", "Cash (ring)", "Solver PLO charts", "Flop: solver solutions. Turn and river: rule-based strategy."],
    ["Pot-Limit Omaha 5 (PLO5)", "Cash (ring)", "Solver PLO charts", "Flop: solver solutions. Turn and river: rule-based strategy."],
], [36, 30, 36, 68]))

story.append(P("Game variants", "h2"))
story.append(table([
    ["Variant", "Support", "Details"],
    ["Antes", "<b>Supported</b>", "Send the ante per player in <font face='Mono'>Ante</font>."],
    ["Straddles", "<b>Supported</b>", "Send the straddle amount in <font face='Mono'>Straddle</font>."],
    ["Run it twice / three times", "<b>Supported</b>",
     "Strategy and player statistics work normally. The winnings recorded in the hand history are not exact, because "
     "<font face='Mono'>HandState</font> describes only one run of the board. Winnings are used only for the bots' win/loss "
     "reports, not for strategy or player statistics."],
    ["Bomb pots", "Works, not optimal",
     "The service makes decisions, but the strategy is not tuned for bomb pots, so expect weaker play in those hands."],
    ["Double-board games", "<b>Not supported</b>", "Do not send double-board hands to the service."],
], [38, 30, 102]))

story.append(P("Baseline strategy plus exploits", "h2"))
story.append(P(
    "Every solver output is a <b>baseline</b>. PDecision keeps a local database of opponent statistics, built from the "
    "hand histories of your PCs, and shifts the baseline against each opponent's measured tendencies. Some examples:"))
story += bullets([
    "<b>An opponent who folds too often to flop bets</b> gets bluffed more often.",
    "<b>An opponent who raises very aggressively</b> faces more value bets and fewer bluffs, so the bot gets raised by worse hands more often.",
    "<b>An opponent who rarely folds</b> faces fewer bluffs and thinner value bets.",
])
story.append(P("These adjustments apply to every game type and format. You do not need to configure anything: the "
               "service looks opponents up by the names you send in each table state."))

story.append(P("What you receive", "h2"))
story += bullets([
    "The <b>PDecision</b> program folder, containing <font face='Mono'>PDecision.exe</font>, its libraries, "
    "<font face='Mono'>appsettings.json</font> and <font face='Mono'>nlog.config</font>. Do not edit the configuration files unless support asks you to.",
    "This guide.",
    "A <b>profile assignment</b> for your bot accounts (section 10).",
])

# ---------------- 2 Installation ----------------
story.append(PageBreak())
story.append(P("2. System requirements and installation", "h1"))
story.append(P("Requirements", "h2"))
story.append(table([
    ["Item", "Requirement"],
    ["Operating system", "Windows 10 or Windows 11, 64-bit"],
    ["Runtime", ".NET 8 Desktop Runtime (x64)"],
    ["Database", "Microsoft SQL Server Express <b>LocalDB</b> (2019 or later), default instance <font face='Mono'>MSSQLLocalDB</font>"],
    ["Network", "Permanent internet connection. Outbound HTTPS (TCP 443) for cloud storage and the provider backend, "
                "and outbound AMQPS (TCP 5671) for the live solver queue. Locally, the REST API listens on TCP port 5005."],
    ["Disk", "SSD recommended, with at least 10 GB free. Strategy resources take about 6 GB, and the player-statistics "
             "databases grow with the number of hands your bots play."],
    ["User account", "Run PDecision and LocalDB under the <b>same Windows user</b>. The database files are attached "
                     "with Windows integrated security."],
], [38, 132]))

story.append(P("Step 1: install the .NET 8 Desktop Runtime", "h2"))
story.append(P("Download the <b>.NET Desktop Runtime 8.x (x64)</b> installer from Microsoft (dotnet.microsoft.com) and run it."))

story.append(P("Step 2: install SQL Server Express LocalDB", "h2"))
story.append(P("PDecision keeps its player statistics in LocalDB databases on your PC. LocalDB is a free, "
               "lightweight edition of SQL Server that starts on demand. It does not run as a permanent Windows service."))
story += steps([
    "Download the <b>SQL Server Express</b> installer from Microsoft.",
    "Choose <b>Download Media</b>, select the <b>LocalDB</b> package, and run the downloaded <font face='Mono'>SqlLocalDB.msi</font>.",
    "Open a Command Prompt and check that the default instance exists:",
])
story.append(code("""
C:\\> sqllocaldb info
MSSQLLocalDB

C:\\> sqllocaldb start MSSQLLocalDB
LocalDB instance "MSSQLLocalDB" started.
"""))
story.append(P("You do not need to create any databases yourself. On the first start PDecision creates empty statistics "
               "databases in LocalDB and fills them from the hand histories."))

story.append(P("Step 3: install PDecision", "h2"))
story += steps([
    "Copy the PDecision folder to a local disk, for example <font face='Mono'>C:\PDecision\</font>.",
    "Allow <font face='Mono'>PDecision.exe</font> through your firewall and antivirus for outbound connections.",
])

story.append(P("Step 4: first start and PC registration", "h2"))
story.append(P(
    "PDecision only runs on PCs that are registered for your subscription. Each PC is identified by the MAC address of its "
    "network adapter. The registration also tells the service everything about the poker site your bots play on that PC, "
    "so you never configure or send any site information yourself."))
story += steps([
    "Start <font face='Mono'>PDecision.exe</font>.",
    "On a PC that is not registered yet, the service does not start. Instead it shows the window <i>PC not registered</i> "
    "with the MAC address of this PC (or several, if the PC has more than one network adapter).",
    "Click <b>Copy</b> and send the address(es) to your administrator, together with the poker site your bots play on this PC.",
    "Wait until the administrator confirms that the PC is whitelisted. Then close PDecision and start it again.",
])
REG_IMG = os.path.join(os.path.dirname(PROFILE_IMG), "registration.png")
story += [Image(REG_IMG, width=110 * mm, height=110 * mm * 367 / 688), Spacer(1, 3),
          P("The window shown on a PC that is not registered yet.", "small"), Spacer(1, 6)]
story.append(note("Repeat this on every PC that runs PDecision. Each PC is registered once and keeps working after restarts "
                  "and IP address changes. While a PC is not registered, <font face='Mono'>GET /api/health</font> also reports "
                  "<font face='Mono'>failed</font> with the MAC addresses in its <font face='Mono'>error</font> field. Without starting "
                  "PDecision you can find the address with <font face='Mono'>getmac /v /fo list</font> (the <b>Physical Address</b> of "
                  "the Ethernet or Wi-Fi adapter)."))

story.append(P("On a registered PC, the status bar shows progress and the window title the version.", "body"))
story.append(P("At startup the service runs these steps:"))
story += bullets([
    "Starts the REST API. From this moment <font face='Mono'>GET /api/health</font> answers, with status "
    "<font face='Mono'>initializing</font> until the service is ready.",
    "Registers with the provider backend. It then sends a heartbeat every 30 seconds while running.",
    "Downloads any strategy resources that are missing or outdated into <font face='Mono'>C:\\PokerSuite\\</font>. "
    "<b>The first start takes about 30 minutes</b>, because about 6 GB in roughly 1.3 million files are downloaded. "
    "Later starts only check versions, which takes a few seconds.",
    "Creates any missing statistics databases and loads the player statistics into memory.",
    "Runs self-tests: a database round trip, a hand-history import, and a sample NLH decision and PLO decision.",
    "Switches to <font face='Mono'>ready</font>. The status bar shows <font face='Mono'>Done initializing DM: N s</font> "
    "and the API now accepts decision and hand-history requests.",
])
story.append(note("If a self-test fails, the service shows <i>Error initializing Decision Maker</i>, "
                  "<font face='Mono'>/api/health</font> reports <font face='Mono'>failed</font>, and the service does "
                  "<b>not</b> accept requests. Check the log file (section 11) and contact support."))

story.append(P("Folders used on the PC", "h2"))
story.append(table([
    ["Folder", "Purpose"],
    ["C:\\PokerSuite\\", "Strategy resources (charts, solver solutions, rules), downloaded and kept up to date automatically"],
    ["C:\\PokerSuite\\db\\", "LocalDB database files (<font face='Mono'>*.mdf</font>) holding player statistics"],
    ["C:\\PokerSuite\\Logs\\", "Service logs. Files rotate at 5 MB."],
    ["C:\\PokerSuite\\archives\\", "Rotated log archives (the last 3 are kept)"],
], [50, 120], mono_cols=(0,)))
story.append(P("These folder locations are fixed. The Windows user that runs PDecision needs read and write access to them.", "small"))

# ---------------- 3 Hand history sync ----------------
story.append(P("3. Hand histories, player statistics and privacy", "h1"))
story.append(P(
    "Exploits are only as good as the statistics behind them. PDecision therefore pools the hand histories of <b>all PCs "
    "registered to your subscription</b>, so each of your PCs makes decisions with the same, up-to-date picture of every opponent: "
    "the statistics database on each of your PCs holds the same player statistics."))

story.append(P("Your data is private", "h2"))
story += bullets([
    "Your hand histories and statistics belong to your subscription only. They are <b>never shared with other customers</b>, "
    "and your statistics are never mixed with hands from other customers.",
    "Sharing happens only between the PCs registered to your subscription (section 2, step 4).",
    "In the cloud, your hand histories are stored in a private area of your subscription that only your PCs read from.",
])

flow = Table([[
    Paragraph("<b>1. Capture</b><br/>Your bot sends every table state to <font face='Mono'>POST /api/handhistory</font>.", ss["cell"]),
    Paragraph("<b>2. Record</b><br/>PDecision builds the complete hand history and adds it to the statistics on this PC.", ss["cell"]),
    Paragraph("<b>3. Upload</b><br/>The hand is uploaded to your subscription's private cloud storage.", ss["cell"]),
    Paragraph("<b>4. Download</b><br/>Your other PCs download the new hands every few minutes.", ss["cell"]),
    Paragraph("<b>5. Update stats</b><br/>They add them to their statistics, so all your PCs have the same player stats.", ss["cell"]),
]], colWidths=[34 * mm] * 5)
flow.setStyle(TableStyle([
    ("BACKGROUND", (0, 0), (-1, -1), HEADBG),
    ("LINEBEFORE", (1, 0), (-1, -1), 2, colors.white),
    ("LINEABOVE", (0, 0), (-1, 0), 2.5, ACCENT),
    ("VALIGN", (0, 0), (-1, -1), "TOP"),
    ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
    ("TOPPADDING", (0, 0), (-1, -1), 7), ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
]))
story += [flow, Spacer(1, 10)]

story.append(P("What this means for you", "h2"))
story += bullets([
    "<b>Always feed <font face='Mono'>/api/handhistory</font></b>, on every table and for every hand, even when it is not the bot's turn. "
    "Hands that never reach the service are missing from your statistics on all your PCs.",
    "<b>Send showdown and winnings information</b> at the end of each hand (<font face='Mono'>Showdowns</font>, "
    "<font face='Mono'>Winners</font>). Showdown hands are the most valuable data for opponent ranges.",
    "<b>Keep PCs online.</b> A PC that is offline neither uploads its hands nor receives the hands of your other PCs. "
    "It catches up when it reconnects.",
    "<b>Several of your bots at the same table</b> describe the same hand. It is stored once.",
    "<b>Your bots' own accounts</b> are recognised as bots and are not exploited like unknown opponents.",
])
story.append(note("A hand is written when the <b>next</b> hand starts on the same table, that is, when a new "
                  "<font face='Mono'>HandID</font> arrives. The last hand before a table closes is saved when the "
                  "service receives the next hand on that table."))

# ---------------- 4 Integration ----------------
story.append(PageBreak())
story.append(P("4. REST API", "h1"))
story.append(P(
    "Your bots talk to PDecision over <b>HTTP with JSON</b>. Any language with an HTTP client can use it, and you can "
    "try every call with tools such as curl or Postman. One running service handles <b>all your bots and all their tables</b>: "
    "there is no fixed number of tables, and one bot account can play several tables at the same time."))

story.append(P("Base address", "h2"))
story.append(code("""
http://127.0.0.1:5005
"""))
story += bullets([
    "By default the service only listens on the local PC (<font face='Mono'>127.0.0.1</font>), so your bots must run on the same PC.",
    "To use another port, change <font face='Mono'>Api:Urls</font> in <font face='Mono'>appsettings.json</font>.",
    "To reach the service from other PCs, set <font face='Mono'>Api:Urls</font> to <font face='Mono'>http://0.0.0.0:5005</font>, "
    "allow the port in the Windows firewall, and <b>always set an API key</b> (below).",
])

story.append(P("Endpoints", "h2"))
story.append(table([
    ["Method and path", "Body", "Purpose"],
    ["GET /api/health", "none", "Service status. Use it to wait until the service is ready after a start."],
    ["POST /api/decision", "HandState", "<b>Decision request.</b> Send the table state when it is the hero's turn. The reply is a "
                                        "<font face='Mono'>Decision</font> (section 7)."],
    ["POST /api/handhistory", "HandState", "<b>Hand-history feed.</b> Send every table state you capture, on every table, "
                                           "also when it is not the hero's turn (section 3)."],
], [44, 22, 104], mono_cols=(0, 1)))
story.append(P("Send the body as UTF-8 JSON with the header <font face='Mono'>Content-Type: application/json</font>. "
               "The <font face='Mono'>HandState</font> fields are described in section 5.", "body"))

story.append(P("Replies", "h2"))
story.append(P("Every reply is JSON. The HTTP status code tells you the outcome:", "body"))
story.append(table([
    ["Endpoint", "Status", "Body", "Meaning"],
    ["/api/health", "200", "{\"status\":\"ready\",\"version\":\"1.0.0.20\"}", "Ready for requests."],
    ["/api/health", "503", "{\"status\":\"initializing\",CONFIGURE_LOCALLY}", "Still starting up. Try again in a few seconds. "
                                                              "<font face='Mono'>\"failed\"</font> means the self-tests failed (section 11)."],
    ["/api/decision", "200", "{\"Phase\":1,\"BetSize\":2.5,\"Action\":4,CONFIGURE_LOCALLY}", "The decision. Act on it straight away (section 8)."],
    ["/api/handhistory", "200", "{\"accepted\":true}", "The state was added to the hand history."],
    ["/api/handhistory", "200", "{\"accepted\":false}", "The state was skipped as inconsistent, for example a frame captured "
                                                        "mid-animation. This is normal now and then. Just keep sending the following frames."],
    ["both POST", "400", "{\"error\":\"TableID is empty\"}", "The body could not be used: empty, not valid JSON, empty "
                                                             "<font face='Mono'>TableID</font> or empty <font face='Mono'>OriginalNames[0]</font>. "
                                                             "The message says which."],
    ["any", "401", "{\"error\":\"CONFIGURE_LOCALLY\"}", "An API key is configured and the <font face='Mono'>X-Api-Key</font> header is missing or wrong."],
    ["both POST", "503", "{\"error\":\"service is initializing\"}", "The service is not ready yet."],
    ["both POST", "500", "{\"error\":\"internal error, CONFIGURE_LOCALLY\"}", "Unexpected error. Use your fallback action and check the log."],
], [33, 14, 52, 71], mono_cols=(0, 1, 2), keep=False))

story.append(P("Tables, bots and parallel requests", "h2"))
story += bullets([
    "Send the same <font face='Mono'>TableID</font> for a table for as long as it is open. The service keeps the strategy "
    "state per <b>table and bot</b>: the <font face='Mono'>TableID</font> together with the bot's account name in "
    "<font face='Mono'>OriginalNames[0]</font>. This state includes the hand history, opponent reads and the hero's line in the current hand.",
    "<b>One bot on several tables:</b> use a different <font face='Mono'>TableID</font> for each table. Requests for different "
    "tables are processed in parallel, so a slow decision on one table never delays another.",
    "<b>Several of your bots at the same table:</b> each bot sends its own states, with itself as seat 0 and its own name in "
    "<font face='Mono'>OriginalNames[0]</font>. They may use the same <font face='Mono'>TableID</font>.",
    "Requests for the <b>same table and bot</b> are processed one after another, in the order they arrive.",
    "Send hand-history calls without waiting on them in your decision path. A hand-history call that starts a new hand "
    "can take a few seconds, because the previous hand is written and uploaded then.",
])
story.append(note("Set the HTTP timeout for <font face='Mono'>/api/decision</font> to at least <b>30 seconds</b>. The reply "
                  "already includes the human-like waiting time (section 8), and a live solve on the turn or river can add several seconds."))

story.append(P("API key (optional)", "h2"))
story.append(P("If <font face='Mono'>Api:ApiKey</font> in <font face='Mono'>appsettings.json</font> is set, every request except "
               "<font face='Mono'>/api/health</font> must send it in the <font face='Mono'>X-Api-Key</font> header. Leave it empty "
               "when bots and service run on the same PC with the default <font face='Mono'>127.0.0.1</font> address."))
story.append(code("""
"Api": {
  "Urls": "http://127.0.0.1:5005",
  "ApiKey": ""
}
"""))
story.append(P("Restart the service after changing these settings. Leave the other settings in the file unchanged.", "small"))

# ---------------- 5 HandState ----------------
story.append(P("5. Request message: HandState", "h1"))
story.append(P(
    "Both POST endpoints take the same <font face='Mono'>HandState</font> object. Property names are case-sensitive PascalCase, as shown below. "
    "Enumerations are sent as <b>integers</b> (section 6). Money values (bets, stacks, pot, blinds) are decimal numbers in the "
    "table's currency or chips. Use the same unit for all of them."))

story.append(P("Seat-indexed arrays", "h2"))
story.append(P(
    "Every list marked <i>per seat</i> has one entry for <b>each seat at the table</b>, empty seats included. "
    "So a 6-max table always has 6 entries. <b>Index 0 is always the hero</b>, the bot you are deciding for. "
    "The other seats follow in clockwise order, which is the order of action."))

R = "<font color='#b0006b'><b>required</b></font>"
O = "optional"
story.append(table([
    ["Field", "Type", "", "Description"],
    ["TableID", "string", R, "Your unique ID for the table. Keep it the same for the whole session at that table."],
    ["TableName", "string", O, "Human-readable table name, used only for display."],
    ["TableHandle", "int", O, "Window handle or other number for the table. It is used to name the hand-history file."],
    ["HandID", "string", R, "The site's hand number. A new value means a new hand."],
    ["IsNewHand", "bool", R, "<font face='Mono'>true</font> on the first state of each new hand, <font face='Mono'>false</font> after that."],
    ["IsMyTurn", "bool", R, "<font face='Mono'>true</font> when the hero has to act now."],
    ["GameType", "int", R, "2 = NLH, 4 = PLO4, 5 = PLO5."],
    ["TableType", "int", R, "1 = Cash, 2 = SNG, 3 = MTT."],
    ["BBSize / SBSize", "number", R, "Big blind and small blind."],
    ["Ante", "number", R, "Ante for each player. 0 if there is none."],
    ["Straddle", "number", R, "Straddle amount. 0 if there is none."],
    ["DealerID", "int", R, "Seat index (0 = hero) of the dealer button."],
    ["Names", "string[]", R, "Per seat: player screen names. Use <font face='Mono'>\"\"</font> for empty seats. Names[0] is the hero."],
    ["OriginalNames", "string[]", R, "Per seat: the names exactly as shown at the table. In most cases this is identical to Names. "
                                     "<b>OriginalNames[0] must be the bot's account name</b>, because the profile is looked up by that name."],
    ["EmptySeats", "bool[]", R, "Per seat: <font face='Mono'>true</font> if nobody is sitting there."],
    ["InGame", "bool[]", R, "Per seat: <font face='Mono'>true</font> if the player was dealt in and has not folded."],
    ["Stacks", "number[]", R, "Per seat: chips <b>behind</b>, not counting the chips in front of the player on this street."],
    ["Bets", "number[]", R, "Per seat: chips in front of the player on the <b>current street</b>, including posted blinds."],
    ["Pot", "number", R, "Chips collected in the middle from <b>previous streets</b>, not counting the current street's Bets. 0 preflop."],
    ["Cards", "string[7]", R, "Index 0-1: the hero's hole cards in NLH (use <font face='Mono'>\"\"</font> in PLO). Index 2-4: flop. 5: turn. 6: river. "
                              "Use <font face='Mono'>\"\"</font> for any card not dealt yet. "
                              "Each card is rank then suit, for example <font face='Mono'>\"as\"</font>, <font face='Mono'>\"td\"</font>, "
                              "<font face='Mono'>\"9h\"</font> (ranks 2-9 t j q k a, suits s h d c); the service converts upper case to lower case."],
    ["OmahaCards", "string[]", "PLO", "The hero's hole cards in PLO: 4 cards for PLO4, 5 for PLO5. Empty list for NLH."],
    ["Showdowns", "string[]", O, "Per seat: cards shown at showdown, concatenated, for example <font face='Mono'>\"ahkd\"</font>. "
                                 "Otherwise <font face='Mono'>\"\"</font>. Strongly recommended."],
    ["Winners", "number[]", O, "Per seat: amount won at the end of the hand. Strongly recommended."],
    ["PlayersLeft", "int", "SNG/MTT", "Players still in the tournament. <b>Must be kept up to date in tournaments</b>, because it drives "
                                      "ICM (see Tournaments below). 0 in cash games."],
    ["PlayersTotal", "int", "SNG/MTT", "Players who started the tournament. 0 in cash games."],
    ["NumPlacesPaid", "int", "SNG/MTT", "Number of paid places (the payout structure). <b>Set it in tournaments</b>: it drives ICM. "
                                        "0 in cash games. If it is 0 in a tournament, the service estimates 20% of entrants."],
], [30, 18, 17, 105], mono_cols=(0, 1)))
story.append(note("There is <b>no field for the poker site</b>. The site is part of each PC's registration "
                  "(section 2, step 4), so you never send it."))
story.append(P("Any other properties of the object (for example <font face='Mono'>IsFoldVisible</font>, "
               "<font face='Mono'>Amount</font>, <font face='Mono'>UserName</font>) are ignored by the service and can be left out.", "small"))

story.append(P("Tournaments (SNG/MTT)", "h2"))
story.append(P(
    "In tournaments, chips are not worth the same as money: losing a stack near the money or at the final table costs more "
    "than winning the same amount gains. The service accounts for this with <b>ICM</b> (Independent Chip Model) pressure, "
    "which makes the bots tighter or looser depending on how close the payouts are."))
story.append(P("ICM is calculated from three inputs, so all three must be correct in every tournament request:"))
story += bullets([
    "<font face='Mono'>PlayersLeft</font>: the players still in the tournament, updated as players bust.",
    "<font face='Mono'>NumPlacesPaid</font>: how many places are paid.",
    "<font face='Mono'>Stacks</font>: the stack sizes of the players at the table.",
])
story.append(P("Also set <font face='Mono'>TableType</font> to 2 (SNG) or 3 (MTT) and <font face='Mono'>PlayersTotal</font> to "
               "the number of entrants. If these values are wrong or out of date, the ICM pressure is wrong too, and the bots play "
               "too tight or too loose near the money and at the final table."))

story.append(P("Card format", "h2"))
story.append(P("Each card is two <b>lowercase</b> characters: the rank, then the suit."))
story.append(table([
    ["", "Values"],
    ["Rank", "<font face='Mono'>2 3 4 5 6 7 8 9 t j q k a</font>"],
    ["Suit", "<font face='Mono'>c</font> clubs, <font face='Mono'>d</font> diamonds, <font face='Mono'>h</font> hearts, <font face='Mono'>s</font> spades"],
    ["Examples", "<font face='Mono'>as</font> = A\u2660, <font face='Mono'>td</font> = 10\u2666, <font face='Mono'>7h</font> = 7\u2665"],
], [30, 140]))

story.append(P("Consistency rules", "h2"))
story.append(P("The service checks each state against the previous one for the same table. States that break these rules are "
               "rejected (<font face='Mono'>/api/handhistory</font> replies <font face='Mono'>{\"accepted\":false}</font>). They usually come from a screen captured during an animation."))
story += bullets([
    "All 3 flop cards appear together. The turn appears only after the flop, and the river only after the turn.",
    "On the same street, a player's bet never goes down and their stack never goes up.",
    "If a player's bet goes up, their stack must go down by the same amount.",
    "A folded player (<font face='Mono'>InGame=false</font>) cannot come back into the same hand.",
    "The pot does not shrink, and the dealer and cards do not change within a hand.",
    "A new <font face='Mono'>HandID</font> must come with <font face='Mono'>IsNewHand=true</font>.",
    "Card and name lists contain no duplicates. The hero's hole cards are present.",
])
story.append(P("Values are rounded to 2 decimal places on arrival.", "small"))

# ---------------- 6 Enums ----------------
story.append(PageBreak())
story.append(P("6. Enumerations", "h1"))
story.append(P("enGameType (HandState.GameType)", "h2"))
story.append(table([["Value", "Game"], ["2", "NLH"], ["4", "PLO4"], ["5", "PLO5"]], [18, 152], mono_cols=(0,)))
story.append(P("enTableType (HandState.TableType)", "h2"))
story.append(table([["Value", "Format"], ["1", "Cash (ring game)"], ["2", "SNG"], ["3", "MTT"]], [18, 152], mono_cols=(0,)))
story.append(P("enGamePhase (Decision.Phase)", "h2"))
story.append(table([["Value", "Street"], ["1", "PreFlop"], ["2", "Flop"], ["3", "Turn"], ["4", "River"]], [18, 152], mono_cols=(0,)))
story.append(P("EnumDecisionType (Decision.Action)", "h2"))
story.append(table([
    ["Value", "Action", "What to do"],
    ["0", "FOLD", "Fold."],
    ["1", "CALL", "Call. BetSize is the amount you will have in front of you after calling."],
    ["2", "CHECK", "Check."],
    ["3", "BET", "Bet. BetSize is the bet amount."],
    ["4", "RAISE", "Raise <b>to</b> BetSize."],
    ["5", "NONE", "No action, because it is not the hero's turn."],
    ["6", "Exception", "The service could not decide. Use your fallback: check if possible, otherwise fold."],
], [18, 26, 126], mono_cols=(0, 1)))

# ---------------- 7 Decision ----------------
story.append(P("7. Reply message: Decision", "h1"))
story.append(table([
    ["Field", "Type", "Description"],
    ["Action", "int", "The action to take (EnumDecisionType)."],
    ["BetSize", "number", "The <b>total amount the hero has in front of them on this street after acting</b>, in the same unit as "
                          "the request: \"raise to\", not \"raise by\". 0 for FOLD and CHECK. "
                          "For CALL it equals the largest bet on the table, or the hero's whole stack if that is smaller."],
    ["Phase", "int", "The street this decision is for (enGamePhase)."],
    ["DurationMS", "int", "How long the strategy calculation took, in milliseconds."],
    ["RandomTimingMS", "int", "The artificial human-like wait that was added, in milliseconds (section 8)."],
    ["TotalDuration", "int", "The total time from receiving the request to sending the reply, in milliseconds. It includes RandomTimingMS."],
], [30, 18, 122], mono_cols=(0, 1)))
story.append(P("To turn BetSize into the amount to type into the site's bet box, use:", "body"))
story += bullets([
    "Sites with a \"raise to\" box: enter <font face='Mono'>BetSize</font>.",
    "Sites with an \"add chips\" box: enter <font face='Mono'>BetSize \u2212 Bets[0]</font>.",
    "If BetSize is at least the hero's <font face='Mono'>Stacks[0] + Bets[0]</font>, go all-in.",
])

# ---------------- 8 Timing ----------------
story.append(P("8. Human-like timing", "h1"))
story.append(P(
    "Bots that act instantly, or always at the same speed, are easy to spot. PDecision therefore makes the reaction time "
    "vary in a human way, depending on how hard the spot is, the street, the size of the pot and the site. On top of that, "
    "every bot's speed drifts over a session, as if the player were occasionally distracted."))
story.append(P(
    "<b>The wait happens inside the service, before it replies.</b> When the reply arrives, the human-like delay has "
    "already passed. Your bot should then click the action <b>straight away</b>, without adding a delay of its own. "
    "<font face='Mono'>RandomTimingMS</font> and <font face='Mono'>TotalDuration</font> tell you how long the decision "
    "took, for your logs and statistics."))
story.append(note("The longest added wait is about 3 to 15 seconds, depending on the site and the size of the decision. "
                  "This is why the HTTP timeout for <font face='Mono'>/api/decision</font> must be generous (30 s or more). Make sure the site's own "
                  "time bank is longer than this."))

# ---------------- Example ----------------
story.append(PageBreak())
story.append(P("9. Examples and sample code", "h1"))
story.append(P("Worked example (NLH, cash game)", "h2"))
story.append(P(
    "6-max table, 3 players seated. The hero is on the button (seat 0) with A\u2660A\u2666. Seat 1 has posted the small "
    "blind and seat 2 the big blind. It is the hero's turn preflop. This is the body of "
    "<font face='Mono'>POST /api/decision</font>, and the same body goes to <font face='Mono'>POST /api/handhistory</font>."))
story.append(code("""
{
  "TableID": "clubgg-118842", "TableName": "Rome 0.5/1", "TableHandle": 0,
  "HandID": "8812345", "IsNewHand": true, "IsMyTurn": true,
  "GameType": 2, "TableType": 1,
  "BBSize": 1.0, "SBSize": 0.5, "Ante": 0, "Straddle": 0,
  "DealerID": 0,
  "Names":         ["my_bot_01", "Peter", "Franc", "", "", ""],
  "OriginalNames": ["my_bot_01", "Peter", "Franc", "", "", ""],
  "EmptySeats": [false, false, false, true,  true,  true ],
  "InGame":     [true,  true,  true,  false, false, false],
  "Stacks":     [100.0, 99.5,  99.0,  0, 0, 0],
  "Bets":       [0,     0.5,   1.0,   0, 0, 0],
  "Pot": 0,
  "Cards": ["as", "ad", "", "", "", "", ""],
  "OmahaCards": [],
  "Showdowns": ["", "", "", "", "", ""],
  "Winners":   [0, 0, 0, 0, 0, 0],
  "PlayersLeft": 0, "PlayersTotal": 0, "NumPlacesPaid": 0
}
"""))
story.append(P("Reply: raise to 2.5 big blinds, sent after a 1.85-second human-like wait.", "body"))
story.append(code("""
{"Phase":1,"BetSize":2.5,"Action":4,"DurationMS":38,"RandomTimingMS":1850,"TotalDuration":1902}
"""))
story.append(P("Flop example: both blinds called, so the pot now holds 7.5 and the street bets are back to 0. "
               "The flop is Q\u2663 T\u2663 2\u2660. Change only the fields that changed:", "body"))
story.append(code("""
"IsNewHand": false, "IsMyTurn": true,
"Stacks": [97.5, 97.5, 97.5, 0, 0, 0], "Bets": [0, 0, 0, 0, 0, 0], "Pot": 7.5,
"Cards": ["as", "ad", "qc", "tc", "2s", "", ""]
"""))
story.append(P("PLO note: put the hero's 4 or 5 hole cards in <font face='Mono'>OmahaCards</font> and leave "
               "<font face='Mono'>Cards[0]</font> and <font face='Mono'>Cards[1]</font> as <font face='Mono'>\"\"</font>. "
               "Board cards always go in <font face='Mono'>Cards[2..6]</font>.", "body"))

story.append(P("Recommended bot loop", "h2"))
story += steps([
    "At start, call <font face='Mono'>GET /api/health</font> every few seconds until it returns 200 with status "
    "<font face='Mono'>ready</font>.",
    "Capture the table on every screen change.",
    "Send <b>every</b> captured state to <font face='Mono'>POST /api/handhistory</font>, without blocking the decision path. "
    "An <font face='Mono'>{\"accepted\":false}</font> reply needs no action.",
    "If <font face='Mono'>IsMyTurn</font> is true and you have not yet asked for a decision on this state, send it to "
    "<font face='Mono'>POST /api/decision</font> and wait for the reply.",
    "Click the action from the reply straight away.",
    "On an error status, <font face='Mono'>Exception</font>, or a timeout: check if possible, otherwise fold.",
])

# ---------------- Sample code ----------------
story.append(PageBreak())
story.append(P("Sample client code", "h2"))
story.append(P("C# (.NET 6 or later)", "h3"))
story.append(code("""
using System.Net.Http.Json;
using System.Text.Json;

// One instance for all bots and tables; HttpClient is safe to use in parallel.
sealed class StrategyClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:5005"),
        Timeout = TimeSpan.FromSeconds(30),     // includes the human-like wait
    };

    // public StrategyClient(string apiKey) => _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

    public async Task WaitUntilReadyAsync()
    {
        while (true)
        {
            try { if ((await _http.GetAsync("/api/health")).IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }      // service not started yet
            await Task.Delay(5000);
        }
    }

    // Default JsonSerializer options keep PascalCase names and numeric enums.
    public async Task<bool> SendHandHistoryAsync(object handState)
    {
        var reply = await _http.PostAsJsonAsync("/api/handhistory", handState);
        if (!reply.IsSuccessStatusCode) return false;
        var body = await reply.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accepted").GetBoolean();
    }

    public async Task<JsonElement?> GetDecisionAsync(object handState)
    {
        var reply = await _http.PostAsJsonAsync("/api/decision", handState);
        if (!reply.IsSuccessStatusCode) return null;          // 400 / 500 / 503: use your fallback
        return await reply.Content.ReadFromJsonAsync<JsonElement>();   // Action, BetSize, Phase, CONFIGURE_LOCALLY
    }
}
"""))
story.append(P("Python 3 (standard library only)", "h3"))
story.append(code("""
import json, time, urllib.request, urllib.error

BASE = "http://127.0.0.1:5005"
HEADERS = {"Content-Type": "application/json"}   # add "X-Api-Key": "CONFIGURE_LOCALLY" if configured

def post(path, hand_state, timeout=30):
    req = urllib.request.Request(BASE + path, json.dumps(hand_state).encode("utf-8"), HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as reply:
            return json.loads(reply.read())
    except (urllib.error.URLError, TimeoutError):
        return None                                # error status or timeout: use your fallback

def wait_until_ready():
    while True:
        try:
            with urllib.request.urlopen(BASE + "/api/health", timeout=5) as reply:
                if json.loads(reply.read())["status"] == "ready":
                    return
        except (urllib.error.URLError, TimeoutError):
            pass
        time.sleep(5)

wait_until_ready()
post("/api/handhistory", state)                    # every frame
decision = post("/api/decision", state)            # only when IsMyTurn is true
"""))
story.append(P("curl (Windows command prompt)", "h3"))
story.append(code("""
curl http://127.0.0.1:5005/api/health
curl -X POST http://127.0.0.1:5005/api/decision -H "Content-Type: application/json" --data @state.json
"""))

# ---------------- 9 Profiles ----------------
story.append(PageBreak())
story.append(P("10. Player profiles", "h1"))
story.append(P(
    "When several of your bots sit in the same games, they must not all play identically. Identical bots are easy to "
    "detect, and identical frequencies are easy for observant opponents to exploit. PDecision therefore gives every bot "
    "account a <b>player profile</b>: a playing style with its own preflop looseness, aggression, 3-bet frequency, "
    "continuation-bet frequency and bet sizing."))
img = Image(PROFILE_IMG, width=170 * mm, height=170 * mm * 937 / 1579)
story += [img, Spacer(1, 4),
          P("The eight profiles and their target statistics: VPIP, PFR, 3-bet and flop C-bet.", "small"), Spacer(1, 6)]

story.append(P("Statistic definitions", "h2"))
story.append(table([
    ["Stat", "Meaning"],
    ["VPIP", "Voluntarily Put money In Pot: how often the player calls or raises preflop. It measures how loose the player is."],
    ["PFR", "Pre-Flop Raise: how often the player raises preflop. The closer PFR is to VPIP, the more aggressive the player."],
    ["3-BET", "How often the player re-raises a preflop raise."],
    ["C-BET", "How often the preflop raiser bets the flop."],
], [20, 150]))

story.append(P("Profile reference", "h2"))
rows = [["Profile", "VPIP", "PFR", "3-bet", "C-bet", "Aggression", "Style"]]
prof = [
    ("Maniac", 60, 40, 20, 80, "Very high", "Plays most hands and raises most of them. Frequent 3-bets and relentless barrelling. Puts constant pressure on the table."),
    ("Agro Fish", 40, 30, 12, 70, "High", "Loose and aggressive, like a recreational player. Wide raising ranges and frequent flop bets."),
    ("Calling Station", 60, 10, 3, 30, "Very low", "Loose and passive. Calls a lot and seldom raises or bluffs. Bets mainly with strong hands."),
    ("Passive Fish", 40, 10, 5, 40, "Low", "Loose and passive, but less extreme than the Calling Station. Limps and calls more than it raises."),
    ("Strong Balanced", 28, 20, 8, 50, "Medium", "Solid regular close to the solver baseline. Balanced value and bluffs on every street."),
    ("Strong Tight", 25, 18, 6, 40, "Medium", "Tight-aggressive regular. Narrower ranges, fewer 3-bets and more selective c-bets."),
    ("Strong Loose", 30, 25, 12, 65, "High", "Loose-aggressive regular. Wider opens, frequent 3-bets and aggressive postflop play."),
    ("Strong Mix", 26, 20, 5, 70, "Medium-high", "Standard preflop with few 3-bets, then a high flop c-bet frequency."),
]
for n, v, p, t, c, a, d in prof:
    rows.append([f"<b>{n}</b>", f"{v}%", f"{p}%", f"{t}%", f"{c}%", a, d])
story.append(table(rows, [27, 12, 11, 12, 12, 23, 73]))
story.append(P("The figures are targets. Each bot's real statistics vary around them, because of normal variance and "
               "because exploits respond to the opponents at the table.", "small"))

story.append(P("Profiles and exploits", "h2"))
story.append(P(
    "The profile sets each bot's <b>default style</b>. Exploitative adjustments based on opponent statistics are applied "
    "<b>on top of</b> the profile. For example, a Strong Tight bot still bluffs more against an opponent who folds too often, "
    "but starts from a tighter base than a Strong Loose bot would."))

story.append(P("Who the profile is played against", "h2"))
story.append(P(
    "You choose against which opponents the bots use their profile, and against which they switch to the "
    "<b>strong strategy</b>, which plays to win as much as possible regardless of the bot's profile."))
story.append(table([
    ["Setting", "Profile strategy against", "Strong strategy against"],
    ["<b>Profile vs everyone</b> (default)", "All players: your other bots and all humans", "Nobody"],
    ["Strong vs all humans", "Your other bots", "All human players"],
    ["Strong vs strong humans", "Your other bots and weak human players", "Strong human players"],
], [50, 60, 60]))
story.append(P(
    "<b>By default the bots play their profile strategy against all players, including all humans.</b> The two other settings "
    "keep the bots' profiles varied where it matters for appearance, while making them win more against human players. "
    "Tell support which setting you want. You do not send it in the API requests."))

story.append(P("How profiles are assigned", "h2"))
story += bullets([
    "Profiles are assigned <b>per bot account</b> (account name on the poker site) in the provider's account settings. "
    "You do not send the profile in the API requests.",
    "When you subscribe, and whenever you add accounts, send support the list of account names for each poker site with the "
    "profile you want for each one. If you have no preference, the provider spreads the profiles across your accounts.",
    "The service identifies the account from <font face='Mono'>OriginalNames[0]</font>. It must exactly match the "
    "registered account name, including upper and lower case.",
    "Accounts without an assignment play the default <b>Strong Balanced</b>-type regular profile.",
    "Good practice: at any one table or site, mix several profiles, and include at least one recreational-style profile "
    "(Agro Fish, Passive Fish, Calling Station or Maniac) among your accounts.",
])

# ---------------- 10 Troubleshooting ----------------
story.append(PageBreak())
story.append(P("11. Troubleshooting", "h1"))
story.append(table([
    ["Symptom", "Likely cause and fix"],
    ["The status bar never reaches <i>Done initializing</i> (health stays <font face='Mono'>initializing</font>)",
     "The first start is still downloading resources, which takes about 30 minutes. Or there is no internet connection, or a firewall blocks ports 443 and 5671. Check the log."],
    ["<i>Error initializing Decision Maker</i> at startup (health reports <font face='Mono'>failed</font>)",
     "A self-test failed. Usually LocalDB is not installed or not running (<font face='Mono'>sqllocaldb start MSSQLLocalDB</font>), "
     "or there is no write access to <font face='Mono'>C:\\PokerSuite\\</font>."],
    ["Health reports <font face='Mono'>failed</font>: <i>this PC is not registered or not active</i>",
     "The PC's MAC address is not registered for your subscription, or the registration was deactivated. Send the MAC "
     "addresses listed in the <font face='Mono'>error</font> field to your administrator (section 2, step 4)."],
    ["Connection refused",
     "PDecision is not running, or listens on another address or port (<font face='Mono'>Api:Urls</font>). If the log says the "
     "REST API failed to start, another program already uses port 5005: choose another port."],
    ["HTTP 503", "The service is still starting. Wait until <font face='Mono'>/api/health</font> returns 200."],
    ["HTTP 400",
     "The body could not be read: wrong property names, enums sent as text, or an empty <font face='Mono'>TableID</font> or "
     "<font face='Mono'>OriginalNames[0]</font>. The <font face='Mono'>error</font> field in the reply says what is wrong."],
    ["HTTP 401", "An API key is configured and the <font face='Mono'>X-Api-Key</font> header is missing or different."],
    ["Decision requests time out",
     "The HTTP timeout is too short. It must cover the human-like wait plus solving time: use 30 seconds or more."],
    ["<font face='Mono'>Action = 6</font> (Exception)",
     "The table state is inconsistent (section 5) or a temporary internal error happened. Use your fallback action and check the log."],
    ["Many <font face='Mono'>{\"accepted\":false}</font> hand-history replies",
     "Frames are being captured during animations, so bets, cards or stacks are only partly updated. Wait until the screen settles before capturing."],
    ["Bet sizes look wrong",
     "Stacks include the current street's bet (they must not), Pot includes the current street's Bets (it must not), or a \"raise by\" box is treated as \"raise to\" (section 7)."],
    ["All bots play alike", "Profiles are not assigned for these accounts, or OriginalNames[0] does not match the registered account name (section 10)."],
], [52, 118]))
story.append(P("Logs", "h3"))
story.append(P("The service writes its log to <font face='Mono'>C:\\PokerSuite\\Logs\\PDecision.log</font>. "
               "When you contact support, include the log file and the <font face='Mono'>HandID</font> and "
               "<font face='Mono'>TableID</font> of the hand in question."))
story.append(P("Shutting down", "h3"))
story.append(P("Close the PDecision window normally, so the service can tell the provider backend it is going offline. "
               "Stop your bots first. While PDecision is closed or restarting, requests fail with a connection error or HTTP 503, "
               "so your bots should use their fallback action and wait for <font face='Mono'>/api/health</font> to report ready again."))


def glue(items):
    out = []
    i = 0
    while i < len(items):
        f = items[i]
        if isinstance(f, Paragraph) and f.style.name in ("h2", "h3") and i + 1 < len(items):
            nxt = items[i + 1]
            inner = list(nxt._content) if isinstance(nxt, KeepTogether) else [nxt]
            out.append(KeepTogether([f] + inner))
            i += 2
            continue
        out.append(f)
        i += 1
    return out


story = glue(story)
doc = Doc(OUT)
doc.multiBuild(story)
print("ok", OUT)
