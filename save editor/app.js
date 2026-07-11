// Global state
let saveSlots = [];
let activeSlot = null; // Contains loaded save data
let selectedSlotName = '';
let selectedSteamId = '';
let activeVarType = 'global'; // 'global' or 'player'

const RANK_NAMES = [
    "Street Rat", "Hoodlum", "Peddler", "Hustler", "Bagman",
    "Enforcer", "Shot Caller", "Block Boss", "Underlord", "Baron", "Kingpin"
];

const BASE_ITEMS_DATABASE = [
    { id: "", name: "Empty" },
    { id: "cash", name: "Cash ($)" },
    { id: "goldenskateboard", name: "Golden Skateboard" },
    { id: "offroadskateboard", name: "Offroad Skateboard" },
    { id: "spraypaint", name: "Spray Paint Can" },
    { id: "electrictrimmers", name: "Trimming Shears" },
    { id: "wateringcan", name: "Watering Can" },
    { id: "growtent", name: "Grow Tent" },
    { id: "seedsandstems", name: "Seeds & Stems" },
    { id: "fertilizer", name: "Fertilizer" },
    { id: "fullspectrumgrowlight", name: "Full Spectrum Grow Light" },
    { id: "airpot", name: "Air Grow Pot" },
    { id: "ducttape", name: "Duct Tape" },
    { id: "ogkush", name: "Weed (OG Kush)" },
    { id: "sourdiesel", name: "Weed (Sour Diesel)" },
    { id: "greencrack", name: "Weed (Green Crack)" },
    { id: "granddaddypurple", name: "Weed (Granddaddy Purple)" },
    { id: "meth", name: "Methamphetamine" },
    { id: "cocaine", name: "Cocaine" },
    { id: "shroom", name: "Magic Shrooms" },
    { id: "cuke", name: "Mixer (Cuke)" },
    { id: "banana", name: "Mixer (Banana)" },
    { id: "paracetamol", name: "Mixer (Paracetamol)" },
    { id: "viagor", name: "Mixer (Viagor)" },
    { id: "donut", name: "Mixer (Donut)" },
    { id: "mouthwash", name: "Mixer (Mouthwash)" },
    { id: "motoroil", name: "Mixer (Motor Oil)" },
    { id: "gasoline", name: "Mixer (Gasoline)" },
    { id: "megabean", name: "Mixer (Mega Bean)" },
    { id: "chili", name: "Mixer (Chili)" }
];

let originalInventorySlots = [];
let originalCrateSlots = {};

function detectDataTypeForId(id) {
    if (!id) return "ItemData";
    const cleanId = id.trim().toLowerCase();
    
    if (cleanId === "cash") return "CashData";
    if (cleanId === "trashgrabber") return "TrashGrabberData";
    
    // Check Weed
    const baseWeed = ['ogkush', 'sourdiesel', 'greencrack', 'granddaddypurple'];
    const customWeed = activeSlot && activeSlot.products && activeSlot.products.CreatedWeed 
        ? activeSlot.products.CreatedWeed.map(w => w.ID.toLowerCase()) 
        : [];
    if (baseWeed.includes(cleanId) || customWeed.includes(cleanId)) return "WeedData";
    
    // Check Meth
    const baseMeth = ['meth'];
    const customMeth = activeSlot && activeSlot.products && activeSlot.products.CreatedMeth 
        ? activeSlot.products.CreatedMeth.map(m => m.ID.toLowerCase()) 
        : [];
    if (baseMeth.includes(cleanId) || customMeth.includes(cleanId)) return "MethData";
    
    // Check Cocaine
    const baseCocaine = ['cocaine'];
    const customCocaine = activeSlot && activeSlot.products && activeSlot.products.CreatedCocaine 
        ? activeSlot.products.CreatedCocaine.map(c => c.ID.toLowerCase()) 
        : [];
    if (baseCocaine.includes(cleanId) || customCocaine.includes(cleanId)) return "CocaineData";
    
    // Check Shrooms
    const baseShroom = ['shroom'];
    const customShroom = activeSlot && activeSlot.products && activeSlot.products.CreatedShrooms 
        ? activeSlot.products.CreatedShrooms.map(s => s.ID.toLowerCase()) 
        : [];
    if (baseShroom.includes(cleanId) || customShroom.includes(cleanId)) return "ShroomData";
    
    return "ItemData";
}

function createNewItemObject(id, qty, quality, cashBal) {
    const dataType = detectDataTypeForId(id);
    const gameVer = activeSlot && activeSlot.metadata ? activeSlot.metadata.GameVersion || "0.4.5f2" : "0.4.5f2";
    
    if (id === "") {
        return { DataType: "ItemData", DataVersion: 0, GameVersion: gameVer, ID: "", Quantity: 0 };
    }
    
    if (dataType === "CashData") {
        return { DataType: "CashData", DataVersion: 0, GameVersion: gameVer, ID: "cash", Quantity: 1, CashBalance: cashBal };
    }
    
    if (dataType === "TrashGrabberData") {
        return { DataType: "TrashGrabberData", DataVersion: 0, GameVersion: gameVer, ID: "trashgrabber", Quantity: 1, Content: { TrashIDs: [], TrashQuantities: [] } };
    }
    
    if (["WeedData", "MethData", "CocaineData", "ShroomData"].includes(dataType)) {
        return { DataType: dataType, DataVersion: 0, GameVersion: gameVer, ID: id, Quantity: qty, Quality: quality, PackagingID: "baggie" };
    }
    
    const itemObj = { DataType: "ItemData", DataVersion: 0, GameVersion: gameVer, ID: id, Quantity: qty };
    if (id !== "goldenskateboard" && id !== "offroadskateboard") {
        itemObj.Quality = quality;
    }
    return itemObj;
}

function rebuildItemsDatalist() {
    const datalist = document.getElementById("all-items-datalist");
    if (!datalist) return;
    
    datalist.innerHTML = "";
    
    const items = [...BASE_ITEMS_DATABASE];
    
    if (activeSlot && activeSlot.products) {
        const weed = activeSlot.products.CreatedWeed || [];
        const meth = activeSlot.products.CreatedMeth || [];
        const cocaine = activeSlot.products.CreatedCocaine || [];
        const shrooms = activeSlot.products.CreatedShrooms || [];
        
        const customStrains = [
            ...weed.map(w => ({ id: w.ID, name: `${w.Name} (Weed Strain)` })),
            ...meth.map(m => ({ id: m.ID, name: `${m.Name} (Meth Strain)` })),
            ...cocaine.map(c => ({ id: c.ID, name: `${c.Name} (Cocaine Strain)` })),
            ...shrooms.map(s => ({ id: s.ID, name: `${s.Name} (Shrooms Strain)` }))
        ];
        
        customStrains.forEach(cs => {
            if (!items.some(i => i.id === cs.id)) {
                items.push(cs);
            }
        });
    }
    
    items.forEach(item => {
        const opt = document.createElement("option");
        opt.value = item.id;
        opt.innerText = item.name;
        datalist.appendChild(opt);
    });
}

const ALL_DRUG_PROPERTIES = [
    "calming", "energizing", "sneaky", "tropicthunder", "slippery", "toxic", 
    "refreshing", "balding", "gingeritis", "sedating", "foggy", "cyclopean", 
    "spicy", "caloriedense"
];

const BIZ_NAMES = {
    "carwash": "Car Wash",
    "laundromat": "Laundromat",
    "postoffice": "Post Office",
    "tacoticklers": "Taco Ticklers"
};

const PROP_NAMES = {
    "barn": "Barn & Greenhouse",
    "seweroffice": "Sewer Office",
    "sweatshop": "Underground Sweatshop",
    "motelroom": "Motel Room Safehouse",
    "manor": "Hyland Manor"
};

function capitalizeString(str) {
    if (!str) return "";
    return str.charAt(0).toUpperCase() + str.slice(1);
}

// XP Progression Formulas
function getXPForTier(rank) {
    return 240 + rank * 195;
}

function rankTierXpToTotalXp(rank, tier, xp) {
    let total = 0;
    // Add all XP from complete ranks below
    for (let r = 0; r < rank; r++) {
        total += 5 * getXPForTier(r);
    }
    // Add complete tiers in current rank
    total += (tier - 1) * getXPForTier(rank);
    // Add current XP
    total += xp;
    return total;
}

function getRankAndTierFromTotalXP(totalXp) {
    let remaining = totalXp;
    let rank = 0;
    let tier = 1;
    let xp = 0;
    
    for (let r = 0; r <= 10; r++) {
        let rankXpLimit = 5 * getXPForTier(r);
        if (remaining >= rankXpLimit) {
            remaining -= rankXpLimit;
            rank = r + 1;
        } else {
            rank = r;
            let tierXpLimit = getXPForTier(r);
            let completedTiers = Math.floor(remaining / tierXpLimit);
            tier = completedTiers + 1;
            xp = remaining % tierXpLimit;
            break;
        }
    }
    
    // Clamp to Kingpin (Rank 10) Tier V if exceeded
    if (rank > 10) {
        rank = 10;
        tier = 5;
        xp = getXPForTier(10);
    }
    
    return { rank, tier, xp };
}

// DOM Elements
document.addEventListener("DOMContentLoaded", () => {
    initApp();
});

function initApp() {
    loadSlots();
    setupNavigation();
    setupEventListeners();
}

// Navigation Tabs
function setupNavigation() {
    const tabs = document.querySelectorAll(".tab-btn");
    tabs.forEach(tab => {
        tab.addEventListener("click", () => {
            tabs.forEach(t => t.classList.remove("active"));
            tab.classList.add("active");
            
            const targetTab = tab.getAttribute("data-tab");
            document.querySelectorAll(".tab-pane").forEach(pane => {
                pane.classList.remove("active");
            });
            document.getElementById(`tab-${targetTab}`).classList.add("active");
        });
    });

    // Variables sub-tabs
    const varTabs = document.querySelectorAll(".var-tab-btn");
    varTabs.forEach(tab => {
        tab.addEventListener("click", () => {
            varTabs.forEach(t => t.classList.remove("active"));
            tab.classList.add("active");
            activeVarType = tab.getAttribute("data-vartype");
            renderVariablesTable();
        });
    });
}

// API: Load list of save slots
async function loadSlots() {
    const listContainer = document.getElementById("slots-list");
    try {
        const res = await fetch("/api/saves");
        saveSlots = await res.json();
        
        if (saveSlots.length === 0) {
            listContainer.innerHTML = '<div class="loading-slots">No saves found in AppData.</div>';
            return;
        }
        
        listContainer.innerHTML = "";
        saveSlots.forEach(slot => {
            const lastPlayed = formatLastPlayed(slot.last_played);
            const card = document.createElement("div");
            card.className = "slot-card";
            card.innerHTML = `
                <div class="slot-card-header">
                    <span class="slot-name">${slot.name}</span>
                    <span class="slot-badge">Rank ${slot.rank}</span>
                </div>
                <div class="slot-info">
                    <span>Balance: $${slot.online_balance.toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}</span>
                    <span class="slot-time">Played: ${lastPlayed}</span>
                </div>
            `;
            card.addEventListener("click", () => {
                document.querySelectorAll(".slot-card").forEach(c => c.classList.remove("active"));
                card.classList.add("active");
                selectSaveSlot(slot.name, slot.steam_id);
            });
            listContainer.appendChild(card);
        });
    } catch (e) {
        console.error("Failed to load slots", e);
        listContainer.innerHTML = '<div class="loading-slots" style="color: var(--color-danger)">Server connection failed.</div>';
    }
}

function formatLastPlayed(dateObj) {
    if (!dateObj || !dateObj.Year) return "Unknown";
    return `${dateObj.Month}/${dateObj.Day}/${dateObj.Year} ${String(dateObj.Hour).padStart(2,'0')}:${String(dateObj.Minute).padStart(2,'0')}`;
}

// API: Fetch single save details
async function selectSaveSlot(slotName, steamId) {
    selectedSlotName = slotName;
    selectedSteamId = steamId;
    
    document.getElementById("empty-state").classList.add("hidden");
    const ws = document.getElementById("editor-workspace");
    ws.classList.remove("hidden");
    
    document.getElementById("active-slot-name").innerText = slotName;
    document.getElementById("active-steam-id").innerText = `Steam ID: ${steamId}`;
    
    try {
        const res = await fetch(`/api/save?slot=${slotName}&steam_id=${steamId}`);
        activeSlot = await res.json();
        
        rebuildItemsDatalist();
        populateOverviewTab();
        populateRankTab();
        populateFinancesTab();
        populatePropertiesTab();
        populateStrainsTab();
        populateRegionsTab();
        populatePlayersTab();
        renderVariablesTable();
        loadBackups();
    } catch (e) {
        showModal("Error", "Failed to load save file data: " + e.message);
    }
}

// Tab Populators
function populateOverviewTab() {
    if (!activeSlot) return;
    
    const lp = activeSlot.metadata.LastPlayedDate || {};
    const cd = activeSlot.metadata.CreationDate || {};
    
    document.getElementById("meta-slot-dir").innerText = selectedSlotName;
    document.getElementById("meta-steam-id").innerText = selectedSteamId;
    document.getElementById("meta-creation").innerText = cd.Year ? `${cd.Month}/${cd.Day}/${cd.Year}` : "N/A";
    document.getElementById("meta-last-played").innerText = formatLastPlayed(lp);
    document.getElementById("meta-game-version").innerText = activeSlot.metadata.GameVersion || "Unknown";
    document.getElementById("meta-tutorial-done").innerText = activeSlot.metadata.PlayTutorial === false ? "Yes" : "No";
    
    // Quick summary card
    const rankVal = activeSlot.rank.Rank || 0;
    const tierVal = activeSlot.rank.Tier || 1;
    
    document.getElementById("overview-rank-selector").value = rankVal;
    document.getElementById("overview-tier-selector").value = tierVal;
    
    const balance = activeSlot.money.OnlineBalance || 0.0;
    const networth = activeSlot.money.Networth || 0.0;
    
    document.getElementById("stat-money-text").innerText = `$${networth.toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}`;
    document.getElementById("stat-online-text").innerText = `Bank: $${balance.toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}`;
}

function populateRankTab() {
    if (!activeSlot) return;
    const r = activeSlot.rank;
    
    document.getElementById("rank-selector").value = r.Rank || 0;
    document.getElementById("tier-selector").value = r.Tier || 1;
    document.getElementById("input-xp").value = r.XP || 0;
    document.getElementById("input-total-xp").value = r.TotalXP || 0;
    
    updateRankGauge();
}

function updateRankGauge() {
    const rank = parseInt(document.getElementById("rank-selector").value);
    const tier = parseInt(document.getElementById("tier-selector").value);
    const xp = parseInt(document.getElementById("input-xp").value);
    
    // Synchronize Overview tab dropdowns
    document.getElementById("overview-rank-selector").value = rank;
    document.getElementById("overview-tier-selector").value = tier;
    
    const xpLimit = getXPForTier(rank);
    document.getElementById("xp-limit-label").innerText = `/ ${xpLimit} XP`;
    
    const percent = Math.min(100, Math.max(0, (xp / xpLimit) * 100));
    document.getElementById("gauge-bar-inner").style.width = `${percent}%`;
    document.getElementById("gauge-rank-title").innerText = `${RANK_NAMES[rank]} ${"I".repeat(tier)}`;
    document.getElementById("gauge-xp-text").innerText = `${xp} / ${xpLimit} XP`;
    
    const totalXp = rankTierXpToTotalXp(rank, tier, xp);
    document.getElementById("gauge-total-xp").innerText = totalXp;
}

function populateInventoryTab(playerCode) {
    if (!activeSlot) return;
    const player = activeSlot.players.find(p => p.code === playerCode);
    if (!player || !player.inventory) return;
    
    const container = document.getElementById("inventory-slots-container");
    container.innerHTML = "";
    
    const items = player.inventory.Items || [];
    originalInventorySlots = [];
    
    for (let i = 0; i < 9; i++) {
        let itemObj = { DataType: "ItemData", ID: "", Quantity: 0 };
        const rawStr = items[i];
        if (rawStr) {
            try {
                itemObj = JSON.parse(rawStr);
            } catch (e) {
                console.error("Failed to parse inventory item at slot " + i, e);
            }
        }
        originalInventorySlots.push(itemObj);
        
        const card = document.createElement("div");
        card.className = "inventory-slot-card";
        card.dataset.index = i;
        
        const activeId = itemObj.ID || "";
        const dataType = itemObj.DataType || "ItemData";
        
        const isCash = dataType === "CashData" || activeId === "cash";
        const cashStyle = isCash ? "" : "display: none;";
        const normalQtyStyle = isCash ? "display: none;" : "";
        
        const hasQuality = activeId !== "" && !isCash && activeId !== "goldenskateboard" && activeId !== "offroadskateboard";
        const qualityStyle = hasQuality ? "" : "display: none;";
        
        card.innerHTML = `
            <div class="slot-hdr">
                <span>SLOT ${i + 1}</span>
                <span class="slot-type-lbl">${dataType}</span>
            </div>
            
            <div class="input-group">
                <label>Item ID / Search</label>
                <input type="text" list="all-items-datalist" class="form-input slot-item-input" value="${escapeHtml(activeId)}" placeholder="Search ID or custom..." oninput="onInventoryItemInputChange(${i}, this.value)">
            </div>
            
            <div class="slot-row-two">
                <div class="input-group slot-qty-wrapper" style="${normalQtyStyle}">
                    <label>Quantity</label>
                    <input type="number" class="form-input slot-quantity" min="0" value="${itemObj.Quantity || 0}">
                </div>
                
                <div class="input-group slot-quality-wrapper" style="${qualityStyle}">
                    <label>Quality</label>
                    <select class="form-input slot-quality">
                        <option value="Trash" ${itemObj.Quality === 'Trash' ? 'selected' : ''}>Trash</option>
                        <option value="Poor" ${itemObj.Quality === 'Poor' ? 'selected' : ''}>Poor</option>
                        <option value="Mediocre" ${itemObj.Quality === 'Mediocre' ? 'selected' : ''}>Mediocre</option>
                        <option value="Good" ${itemObj.Quality === 'Good' ? 'selected' : ''}>Good</option>
                        <option value="Heavenly" ${itemObj.Quality === 'Heavenly' ? 'selected' : ''}>Heavenly</option>
                    </select>
                </div>
            </div>
            
            <div class="input-group slot-cash-wrapper" style="${cashStyle}">
                <label>Cash Balance ($)</label>
                <input type="number" class="form-input slot-cash-balance" step="0.01" value="${itemObj.CashBalance || 0.0}">
            </div>
        `;
        container.appendChild(card);
    }
}

window.onInventoryItemInputChange = function(index, value) {
    const card = document.querySelector(`.inventory-slot-card[data-index="${index}"]`);
    if (!card) return;
    
    const typeLabel = card.querySelector(".slot-type-lbl");
    const qtyWrapper = card.querySelector(".slot-qty-wrapper");
    const qualityWrapper = card.querySelector(".slot-quality-wrapper");
    const cashWrapper = card.querySelector(".slot-cash-wrapper");
    const qtyInput = card.querySelector(".slot-quantity");
    const cashInput = card.querySelector(".slot-cash-balance");
    
    const val = value.trim();
    const dataType = detectDataTypeForId(val);
    
    typeLabel.innerText = dataType;
    qtyWrapper.style.display = "";
    qualityWrapper.style.display = "none";
    cashWrapper.style.display = "none";
    
    if (val === "") {
        qtyInput.value = 0;
    } else if (dataType === "CashData") {
        qtyWrapper.style.display = "none";
        cashWrapper.style.display = "";
        if (parseFloat(cashInput.value) === 0) {
            cashInput.value = 1000.0;
        }
        
        const inputCash = document.getElementById("input-cash-on-hand");
        if (inputCash) {
            inputCash.value = parseFloat(cashInput.value).toFixed(2);
        }
    } else if (val === "goldenskateboard" || val === "offroadskateboard") {
        qtyInput.value = 1;
    } else {
        if (dataType === "ItemData" || ["WeedData", "MethData", "CocaineData", "ShroomData"].includes(dataType)) {
            qualityWrapper.style.display = "";
        }
        if (qtyInput.value == 0) qtyInput.value = 10;
    }
};

function saveActiveInventoryToMemory(playerCode) {
    const targetCode = playerCode || currentActivePlayerCode;
    if (!targetCode || !activeSlot) return;
    
    const player = activeSlot.players.find(p => p.code === targetCode);
    if (!player || !player.inventory) return;
    
    const cards = document.querySelectorAll("#inventory-slots-container .inventory-slot-card");
    const items = [];
    
    cards.forEach(card => {
        const i = parseInt(card.dataset.index);
        const id = card.querySelector(".slot-item-input").value.trim();
        
        let qty = 0;
        let quality = "Good";
        let cashBal = 0.0;
        
        const cashInput = card.querySelector(".slot-cash-balance");
        if (cashInput) cashBal = parseFloat(cashInput.value) || 0.0;
        
        const qtyInput = card.querySelector(".slot-quantity");
        if (qtyInput) qty = parseInt(qtyInput.value) || 0;
        
        const qualSelect = card.querySelector(".slot-quality");
        if (qualSelect) quality = qualSelect.value;
        
        let itemObj = originalInventorySlots[i] || { DataType: "ItemData", ID: "", Quantity: 0 };
        
        if (itemObj.ID !== id) {
            itemObj = createNewItemObject(id, qty, quality, cashBal);
        } else {
            if (itemObj.DataType === "CashData") {
                itemObj.CashBalance = cashBal;
            } else if (itemObj.ID !== "") {
                itemObj.Quantity = qty;
                if (itemObj.Quality !== undefined || (itemObj.ID !== "goldenskateboard" && itemObj.ID !== "offroadskateboard" && itemObj.DataType === "ItemData")) {
                    itemObj.Quality = quality;
                }
            }
        }
        
        originalInventorySlots[i] = itemObj;
        items.push(JSON.stringify(itemObj));
    });
    
    player.inventory.Items = items;
}

function saveActiveCoordinatesToMemory(playerCode) {
    if (!activeSlot) return;
    const targetCode = playerCode || currentActivePlayerCode;
    if (!targetCode) return;
    
    const player = activeSlot.players.find(p => p.code === targetCode);
    if (player && player.player) {
        const xVal = document.getElementById("coord-x").value;
        const yVal = document.getElementById("coord-y").value;
        const zVal = document.getElementById("coord-z").value;
        const rotVal = document.getElementById("coord-rot").value;
        
        if (xVal !== "" && yVal !== "" && zVal !== "") {
            player.player.Position = {
                x: parseFloat(xVal) || 0.0,
                y: parseFloat(yVal) || 0.0,
                z: parseFloat(zVal) || 0.0
            };
            player.player.Rotation = parseFloat(rotVal) || 0.0;
        }
    }
}

function populatePropertiesTab() {
    if (!activeSlot) return;
    
    const bizContainer = document.getElementById("businesses-toggles-container");
    bizContainer.innerHTML = "";
    
    activeSlot.businesses.forEach((biz, index) => {
        const title = BIZ_NAMES[biz.PropertyCode] || biz.PropertyCode;
        const row = document.createElement("div");
        row.className = "property-toggle-row";
        row.innerHTML = `
            <div class="prop-info">
                <span class="prop-title">${title}</span>
                <span class="prop-code-lbl">Code: ${biz.PropertyCode}</span>
            </div>
            <label class="switch">
                <input type="checkbox" id="biz-owned-${index}" ${biz.IsOwned ? 'checked' : ''} onchange="toggleBizOwnedState(${index}, this.checked)">
                <span class="slider"></span>
            </label>
        `;
        bizContainer.appendChild(row);
    });
    
    const propContainer = document.getElementById("properties-toggles-container");
    propContainer.innerHTML = "";
    
    activeSlot.properties.forEach((prop, index) => {
        const title = PROP_NAMES[prop.PropertyCode] || prop.PropertyCode;
        const row = document.createElement("div");
        row.className = "property-toggle-row";
        row.innerHTML = `
            <div class="prop-info">
                <span class="prop-title">${title}</span>
                <span class="prop-code-lbl">Code: ${prop.PropertyCode}</span>
            </div>
            <label class="switch">
                <input type="checkbox" id="prop-owned-${index}" ${prop.IsOwned ? 'checked' : ''} onchange="togglePropOwnedState(${index}, this.checked)">
                <span class="slider"></span>
            </label>
        `;
        propContainer.appendChild(row);
    });
    
    renderStorageCrates();
}

window.toggleBizOwnedState = function(index, checked) {
    activeSlot.businesses[index].IsOwned = checked;
    renderStorageCrates();
};

window.togglePropOwnedState = function(index, checked) {
    activeSlot.properties[index].IsOwned = checked;
    renderStorageCrates();
};

function renderStorageCrates() {
    const container = document.getElementById("storage-crates-container");
    container.innerHTML = "";
    
    let cratesFound = 0;
    
    const locations = [
        ...activeSlot.businesses.map(b => ({ type: 'business', data: b, title: BIZ_NAMES[b.PropertyCode] || b.PropertyCode })),
        ...activeSlot.properties.map(p => ({ type: 'property', data: p, title: PROP_NAMES[p.PropertyCode] || p.PropertyCode }))
    ];
    
    locations.forEach(loc => {
        if (!loc.data.IsOwned) return;
        
        const objects = loc.data.Objects || [];
        objects.forEach((obj, objIndex) => {
            if (obj.DataType === "PlaceableStorageData") {
                cratesFound++;
                let crateData = {};
                try {
                    crateData = JSON.parse(obj.BaseData);
                } catch(e) {
                    console.error("Failed to parse PlaceableStorageData BaseData", e);
                    return;
                }
                
                const contents = crateData.Contents || { Items: [] };
                const items = contents.Items || [];
                const crateId = crateData.ID || "Storage Crate";
                const crateName = crateId === "largestoragerack" ? "Large Storage Rack" : (crateId === "smallstoragerack" ? "Small Storage Rack" : capitalizeString(crateId));
                
                const card = document.createElement("div");
                card.className = "storage-crate-card";
                card.innerHTML = `
                    <h4>${crateName}</h4>
                    <span class="storage-crate-loc">Location: <strong>${loc.title}</strong></span>
                    <div class="crate-slots-list" data-loc-type="${loc.type}" data-loc-code="${loc.data.PropertyCode}" data-obj-index="${objIndex}">
                        <!-- slots dynamic -->
                    </div>
                `;
                
                const slotsList = card.querySelector(".crate-slots-list");
                
                items.forEach((itemStr, slotIndex) => {
                    let itemObj = { DataType: "ItemData", ID: "", Quantity: 0 };
                    if (itemStr) {
                        try {
                            itemObj = JSON.parse(itemStr);
                        } catch(e) {}
                    }
                    
                    const key = `${loc.type}-${loc.data.PropertyCode}-${objIndex}-${slotIndex}`;
                    originalCrateSlots[key] = itemObj;
                    
                    const slotRow = document.createElement("div");
                    slotRow.className = "crate-slot-row";
                    slotRow.dataset.slotIndex = slotIndex;
                    
                    const activeId = itemObj.ID || "";
                    const dataType = itemObj.DataType || "ItemData";
                    const isCash = dataType === "CashData" || activeId === "cash";
                    
                    let col3Html = "";
                    let col4Html = "";
                    let typeLblHtml = `<span class="crate-type-lbl" style="display:none;">${dataType}</span>`;
                    
                    if (isCash) {
                        col3Html = `<input type="number" class="form-input crate-cash-balance" step="0.01" value="${itemObj.CashBalance || 0.0}" placeholder="Balance ($)" style="width:100%;">`;
                        col4Html = `<span style="color:var(--text-muted); font-size:0.75rem; display:block; padding-top:8px; text-align:center;">CashData</span>`;
                    } else {
                        col3Html = `<input type="number" class="form-input crate-quantity" min="0" value="${itemObj.Quantity || 0}" placeholder="Qty" style="width:100%;">`;
                        
                        const hasQuality = activeId !== "" && activeId !== "goldenskateboard" && activeId !== "offroadskateboard" && 
                                           (dataType === "ItemData" || ["WeedData", "MethData", "CocaineData", "ShroomData"].includes(dataType));
                        
                        if (hasQuality) {
                            col4Html = `
                                <select class="form-input crate-quality" style="width:100%;">
                                    <option value="Trash" ${itemObj.Quality === 'Trash' ? 'selected' : ''}>Trash</option>
                                    <option value="Poor" ${itemObj.Quality === 'Poor' ? 'selected' : ''}>Poor</option>
                                    <option value="Mediocre" ${itemObj.Quality === 'Mediocre' ? 'selected' : ''}>Mediocre</option>
                                    <option value="Good" ${itemObj.Quality === 'Good' ? 'selected' : ''}>Good</option>
                                    <option value="Heavenly" ${itemObj.Quality === 'Heavenly' ? 'selected' : ''}>Heavenly</option>
                                </select>
                            `;
                        } else {
                            col4Html = `<span style="color:var(--text-muted); font-size:0.75rem; display:block; padding-top:8px; text-align:center;">${dataType}</span>`;
                        }
                    }
                    
                    slotRow.innerHTML = `
                        <div class="crate-slot-index">${slotIndex + 1}</div>
                        <div>
                            <input type="text" list="all-items-datalist" class="form-input crate-item-input" value="${escapeHtml(activeId)}" placeholder="Search ID" oninput="onCrateItemInputChange(this)">
                            ${typeLblHtml}
                        </div>
                        <div class="col-qty-cash">
                            ${col3Html}
                        </div>
                        <div class="col-quality-type">
                            ${col4Html}
                        </div>
                    `;
                    slotsList.appendChild(slotRow);
                });
                
                container.appendChild(card);
            }
        });
    });
    
    if (cratesFound === 0) {
        container.innerHTML = '<div style="grid-column: 1/-1; text-align: center; color: var(--text-muted); padding: 24px;">No storage crates found. Purchase/Own safehouses or businesses to place and edit containers.</div>';
    }
}

window.onCrateItemInputChange = function(inputEl) {
    const row = inputEl.closest(".crate-slot-row");
    if (!row) return;
    
    const val = inputEl.value.trim();
    const typeLabel = row.querySelector(".crate-type-lbl");
    const colQtyCash = row.querySelector(".col-qty-cash");
    const colQualityType = row.querySelector(".col-quality-type");
    
    const dataType = detectDataTypeForId(val);
    typeLabel.innerText = dataType;
    
    if (val === "") {
        if (colQtyCash.querySelector(".crate-quantity")) {
            colQtyCash.querySelector(".crate-quantity").value = 0;
        }
    } else if (dataType === "CashData") {
        colQtyCash.innerHTML = `<input type="number" class="form-input crate-cash-balance" step="0.01" value="100.0" placeholder="Balance ($)" style="width:100%;">`;
        colQualityType.innerHTML = `<span style="color:var(--text-muted); font-size:0.75rem; display:block; padding-top:8px; text-align:center;">CashData</span>`;
    } else {
        if (!colQtyCash.querySelector(".crate-quantity")) {
            colQtyCash.innerHTML = `<input type="number" class="form-input crate-quantity" min="0" value="10" placeholder="Qty" style="width:100%;">`;
        }
        
        const hasQuality = ["ItemData", "WeedData", "MethData", "CocaineData", "ShroomData"].includes(dataType) && val !== "goldenskateboard" && val !== "offroadskateboard";
        if (hasQuality) {
            colQualityType.innerHTML = `
                <select class="form-input crate-quality" style="width:100%;">
                    <option value="Trash">Trash</option>
                    <option value="Poor">Poor</option>
                    <option value="Mediocre">Mediocre</option>
                    <option value="Good" selected>Good</option>
                    <option value="Heavenly">Heavenly</option>
                </select>
            `;
        } else {
            colQualityType.innerHTML = `<span style="color:var(--text-muted); font-size:0.75rem; display:block; padding-top:8px; text-align:center;">${dataType}</span>`;
        }
        
        const qtyInput = colQtyCash.querySelector(".crate-quantity");
        if (qtyInput && qtyInput.value == 0) {
            qtyInput.value = (val === "goldenskateboard" || val === "offroadskateboard") ? 1 : 10;
        }
    }
};

function saveActiveStorageCratesToMemory() {
    if (!activeSlot) return;
    
    const crateContainers = document.querySelectorAll("#storage-crates-container .crate-slots-list");
    crateContainers.forEach(list => {
        const locType = list.dataset.locType;
        const locCode = list.dataset.locCode;
        const objIndex = parseInt(list.dataset.objIndex);
        
        let prop = null;
        if (locType === 'business') {
            prop = activeSlot.businesses.find(b => b.PropertyCode === locCode);
        } else {
            prop = activeSlot.properties.find(p => p.PropertyCode === locCode);
        }
        
        if (!prop || !prop.Objects || !prop.Objects[objIndex]) return;
        
        const obj = prop.Objects[objIndex];
        let baseData = {};
        try {
            baseData = JSON.parse(obj.BaseData);
        } catch(e) {
            return;
        }
        
        const slotRows = list.querySelectorAll(".crate-slot-row");
        const items = [];
        
        slotRows.forEach(row => {
            const slotIndex = parseInt(row.dataset.slotIndex);
            const id = row.querySelector(".crate-item-input").value.trim();
            
            let qty = 0;
            let quality = "Good";
            let cashBal = 0.0;
            
            const cashInput = row.querySelector(".crate-cash-balance");
            if (cashInput) cashBal = parseFloat(cashInput.value) || 0.0;
            
            const qtyInput = row.querySelector(".crate-quantity");
            if (qtyInput) qty = parseInt(qtyInput.value) || 0;
            
            const qualSelect = row.querySelector(".crate-quality");
            if (qualSelect) quality = qualSelect.value;
            
            const key = `${locType}-${locCode}-${objIndex}-${slotIndex}`;
            let itemObj = originalCrateSlots[key] || { DataType: "ItemData", ID: "", Quantity: 0 };
            
            if (itemObj.ID !== id) {
                itemObj = createNewItemObject(id, qty, quality, cashBal);
            } else {
                if (itemObj.DataType === "CashData") {
                    itemObj.CashBalance = cashBal;
                } else if (itemObj.ID !== "") {
                    itemObj.Quantity = qty;
                    if (itemObj.Quality !== undefined || (itemObj.ID !== "goldenskateboard" && itemObj.ID !== "offroadskateboard" && itemObj.DataType === "ItemData")) {
                        itemObj.Quality = quality;
                    }
                }
            }
            
            originalCrateSlots[key] = itemObj;
            items.push(JSON.stringify(itemObj));
        });
        
        if (!baseData.Contents) baseData.Contents = {};
        baseData.Contents.Items = items;
        obj.BaseData = JSON.stringify(baseData);
    });
}

function populateStrainsTab() {
    if (!activeSlot || !activeSlot.products) return;
    
    const container = document.getElementById("strains-list-container");
    container.innerHTML = "";
    
    const weed = activeSlot.products.CreatedWeed || [];
    const meth = activeSlot.products.CreatedMeth || [];
    const cocaine = activeSlot.products.CreatedCocaine || [];
    const shrooms = activeSlot.products.CreatedShrooms || [];
    
    const allStrains = [
        ...weed.map(w => ({ data: w, type: 'weed', label: 'Weed (Marijuana)' })),
        ...meth.map(m => ({ data: m, type: 'meth', label: 'Methamphetamine' })),
        ...cocaine.map(c => ({ data: c, type: 'cocaine', label: 'Cocaine' })),
        ...shrooms.map(s => ({ data: s, type: 'shrooms', label: 'Magic Shrooms' }))
    ];
    
    if (allStrains.length === 0) {
        container.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 24px;">No custom mixed product strains have been created yet.</div>';
        return;
    }
    
    allStrains.forEach(strain => {
        const sData = strain.data;
        const pricesList = activeSlot.products.ProductPrices || [];
        const priceEntry = pricesList.find(pe => pe.String === sData.ID) || { Int: 0 };
        
        const row = document.createElement("div");
        row.className = "strain-row";
        row.dataset.strainId = sData.ID;
        row.dataset.strainType = strain.type;
        
        let tagsHtml = "";
        const properties = sData.Properties || [];
        properties.forEach((prop, pIndex) => {
            tagsHtml += `
                <span class="property-tag">
                    ${prop}
                    <span class="property-tag-remove" onclick="removeStrainProperty('${strain.type}', '${sData.ID}', ${pIndex})">&times;</span>
                </span>
            `;
        });
        
        let addSelectHtml = `<select class="form-input add-prop-select">`;
        addSelectHtml += `<option value="">+ Add Property...</option>`;
        ALL_DRUG_PROPERTIES.forEach(p => {
            if (!properties.includes(p)) {
                addSelectHtml += `<option value="${p}">${p}</option>`;
            }
        });
        addSelectHtml += `</select>`;
        
        row.innerHTML = `
            <div class="strain-meta-col">
                <span class="slot-type-lbl">${strain.label}</span>
                <span class="strain-id-badge">${sData.ID}</span>
                <div class="input-group" style="margin-top: 8px;">
                    <label>Strain Name</label>
                    <input type="text" class="form-input strain-name-input" value="${escapeHtml(sData.Name || '')}" onchange="updateStrainName('${strain.type}', '${sData.ID}', this.value)">
                </div>
            </div>
            
            <div class="input-group">
                <label>Base Selling Price ($)</label>
                <div class="input-with-button">
                    <input type="number" class="form-input strain-price-input" min="0" value="${priceEntry.Int}" onchange="updateStrainPrice('${sData.ID}', this.value)">
                </div>
            </div>
            
            <div>
                <label>Active Properties</label>
                <div class="properties-editor-tags">
                    ${tagsHtml}
                </div>
                <div class="add-prop-wrapper">
                    ${addSelectHtml}
                    <button type="button" class="btn btn-mini btn-primary" onclick="addStrainProperty('${strain.type}', '${sData.ID}', this)">Add</button>
                </div>
            </div>
        `;
        container.appendChild(row);
    });
}

window.updateStrainName = function(type, id, newName) {
    const listKey = type === 'weed' ? 'CreatedWeed' : (type === 'meth' ? 'CreatedMeth' : (type === 'cocaine' ? 'CreatedCocaine' : 'CreatedShrooms'));
    const list = activeSlot.products[listKey] || [];
    const strain = list.find(s => s.ID === id);
    if (strain) {
        strain.Name = newName;
    }
};

window.updateStrainPrice = function(id, newPrice) {
    const pricesList = activeSlot.products.ProductPrices || [];
    const entry = pricesList.find(pe => pe.String === id);
    if (entry) {
        entry.Int = parseInt(newPrice) || 0;
    } else {
        pricesList.push({ String: id, Int: parseInt(newPrice) || 0 });
    }
};

window.removeStrainProperty = function(type, id, propIndex) {
    const listKey = type === 'weed' ? 'CreatedWeed' : (type === 'meth' ? 'CreatedMeth' : (type === 'cocaine' ? 'CreatedCocaine' : 'CreatedShrooms'));
    const list = activeSlot.products[listKey] || [];
    const strain = list.find(s => s.ID === id);
    if (strain && strain.Properties) {
        strain.Properties.splice(propIndex, 1);
        populateStrainsTab();
    }
};

window.addStrainProperty = function(type, id, btnEl) {
    const wrapper = btnEl.closest(".add-prop-wrapper");
    if (!wrapper) return;
    const selectEl = wrapper.querySelector(".add-prop-select");
    const val = selectEl.value;
    if (!val) return;
    
    const listKey = type === 'weed' ? 'CreatedWeed' : (type === 'meth' ? 'CreatedMeth' : (type === 'cocaine' ? 'CreatedCocaine' : 'CreatedShrooms'));
    const list = activeSlot.products[listKey] || [];
    const strain = list.find(s => s.ID === id);
    if (strain) {
        if (!strain.Properties) strain.Properties = [];
        if (!strain.Properties.includes(val)) {
            strain.Properties.push(val);
            populateStrainsTab();
        }
    }
};

function populateFinancesTab() {
    if (!activeSlot) return;
    const m = activeSlot.money;
    
    document.getElementById("input-online-balance").value = m.OnlineBalance || 0.0;
    document.getElementById("input-networth").value = m.Networth || 0.0;
    document.getElementById("input-lifetime-earnings").value = m.LifetimeEarnings || 0.0;
    document.getElementById("input-weekly-deposit").value = m.WeeklyDepositSum || 0.0;
    
    // Find cash on hand from selected player profile
    const selector = document.getElementById("player-selector");
    const activePlayerCode = selector ? selector.value : "Player_0";
    const player = activeSlot.players.find(p => p.code === activePlayerCode);
    const profileName = activePlayerCode === "Player_0" ? "Default Profile (Player_0)" : `Steam Player (${activePlayerCode})`;
    
    const profileLbl = document.getElementById("cash-on-hand-profile-lbl");
    if (profileLbl) {
        profileLbl.innerText = `Profile: ${profileName}`;
    }
    
    let cashOnHandVal = 0.0;
    if (player && player.inventory && player.inventory.Items) {
        const items = player.inventory.Items;
        for (let i = 0; i < items.length; i++) {
            try {
                const itemObj = JSON.parse(items[i]);
                if (itemObj.ID === "cash" || itemObj.DataType === "CashData") {
                    cashOnHandVal = parseFloat(itemObj.CashBalance) || 0.0;
                    break;
                }
            } catch(e) {}
        }
    }
    const inputCash = document.getElementById("input-cash-on-hand");
    if (inputCash) {
        inputCash.value = cashOnHandVal.toFixed(2);
    }
}

function populateRegionsTab() {
    if (!activeSlot) return;
    const unlocked = activeSlot.rank.UnlockedRegions || [];
    
    const checkboxes = document.querySelectorAll("#regions-checkbox-container input[type='checkbox']");
    checkboxes.forEach(cb => {
        const regionId = parseInt(cb.getAttribute("data-region"));
        cb.checked = unlocked.includes(regionId);
    });
}

function populatePlayersTab() {
    if (!activeSlot) return;
    
    const selector = document.getElementById("player-selector");
    const invSelector = document.getElementById("inventory-player-selector");
    selector.innerHTML = "";
    invSelector.innerHTML = "";
    
    if (activeSlot.players.length === 0) {
        selector.innerHTML = '<option value="">No profiles found</option>';
        invSelector.innerHTML = '<option value="">No profiles found</option>';
        currentActivePlayerCode = "";
        clearCoordsUI();
        return;
    }
    
    activeSlot.players.forEach(p => {
        const name = p.code === "Player_0" ? "Default Profile (Player_0)" : `Steam Player (${p.code})`;
        const opt1 = document.createElement("option");
        opt1.value = p.code;
        opt1.innerText = name;
        selector.appendChild(opt1);
        
        const opt2 = document.createElement("option");
        opt2.value = p.code;
        opt2.innerText = name;
        invSelector.appendChild(opt2);
    });
    
    currentActivePlayerCode = selector.value;
    loadPlayerDetails(selector.value);
    populateInventoryTab(invSelector.value);
}

function loadPlayerDetails(playerCode) {
    const player = activeSlot.players.find(p => p.code === playerCode);
    if (!player) return;
    
    document.getElementById("lbl-player-version").innerText = player.player?.GameVersion || "Unknown";
    document.getElementById("lbl-player-intro").innerText = player.player?.IntroCompleted ? "Completed" : "In Progress";
    
    const inv = player.inventory?.Inventory || [];
    document.getElementById("lbl-player-inventory").innerText = `${inv.length} items`;
    
    // Coords
    const pos = player.player?.Position || { x: 0, y: 0, z: 0 };
    document.getElementById("coord-x").value = pos.x || 0.0;
    document.getElementById("coord-y").value = pos.y || 0.0;
    document.getElementById("coord-z").value = pos.z || 0.0;
    document.getElementById("coord-rot").value = player.player?.Rotation || 0.0;
}

function clearCoordsUI() {
    document.getElementById("lbl-player-version").innerText = "-";
    document.getElementById("lbl-player-intro").innerText = "-";
    document.getElementById("lbl-player-inventory").innerText = "-";
    document.getElementById("coord-x").value = "";
    document.getElementById("coord-y").value = "";
    document.getElementById("coord-z").value = "";
    document.getElementById("coord-rot").value = "";
}

// Variables Tab Rendering & Filtering
function renderVariablesTable() {
    if (!activeSlot) return;
    
    const tbody = document.getElementById("variables-table-body");
    tbody.innerHTML = "";
    
    const filterText = document.getElementById("search-variables").value.toLowerCase();
    
    let variablesList = [];
    if (activeVarType === 'global') {
        variablesList = activeSlot.variables.Variables || [];
    } else {
        const playerCode = document.getElementById("player-selector").value;
        const player = activeSlot.players.find(p => p.code === playerCode);
        if (player && player.variables) {
            variablesList = player.variables.Variables || [];
        }
    }
    
    if (variablesList.length === 0) {
        tbody.innerHTML = '<tr><td colspan="3" style="text-align:center; color:var(--text-muted)">No variables in this category.</td></tr>';
        return;
    }
    
    variablesList.forEach((v, index) => {
        if (filterText && !v.Name.toLowerCase().includes(filterText)) {
            return; // Filtered out
        }
        
        const tr = document.createElement("tr");
        const isBool = v.Value === "True" || v.Value === "False";
        
        let controlHtml = "";
        if (isBool) {
            const checked = v.Value === "True" ? "checked" : "";
            controlHtml = `
                <label class="switch">
                    <input type="checkbox" id="var-val-${index}" ${checked} onchange="updateVariableValue('${activeVarType}', ${index}, this.checked ? 'True' : 'False')">
                    <span class="slider"></span>
                </label>
            `;
        } else {
            controlHtml = `
                <input type="text" id="var-val-${index}" class="form-input form-input-mini" value="${escapeHtml(v.Value)}" onchange="updateVariableValue('${activeVarType}', ${index}, this.value)">
            `;
        }
        
        tr.innerHTML = `
            <td><span class="var-name-lbl">${escapeHtml(v.Name)}</span></td>
            <td>${controlHtml}</td>
            <td><button class="btn btn-mini btn-secondary" onclick="resetVariableToDefault('${activeVarType}', ${index}, '${isBool ? 'False' : '0'}')">Clear</button></td>
        `;
        tbody.appendChild(tr);
    });
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#039;");
}

window.updateVariableValue = function(type, index, newVal) {
    if (type === 'global') {
        activeSlot.variables.Variables[index].Value = String(newVal);
    } else {
        const playerCode = document.getElementById("player-selector").value;
        const player = activeSlot.players.find(p => p.code === playerCode);
        if (player && player.variables) {
            player.variables.Variables[index].Value = String(newVal);
        }
    }
};

window.resetVariableToDefault = function(type, index, defaultVal) {
    const input = document.getElementById(`var-val-${index}`);
    if (input) {
        if (input.type === "checkbox") {
            input.checked = defaultVal === 'True';
        } else {
            input.value = defaultVal;
        }
        updateVariableValue(type, index, defaultVal);
    }
};

// Event Listeners setup
function setupEventListeners() {
    // Rank modifications triggers gauge & XP math
    const rankSel = document.getElementById("rank-selector");
    const tierSel = document.getElementById("tier-selector");
    const xpInput = document.getElementById("input-xp");
    const totalXpInput = document.getElementById("input-total-xp");
    
    const overviewRankSel = document.getElementById("overview-rank-selector");
    const overviewTierSel = document.getElementById("overview-tier-selector");
    
    const updateFromOverviewRankTier = () => {
        rankSel.value = overviewRankSel.value;
        tierSel.value = overviewTierSel.value;
        updateFromRankTierXp();
    };
    
    overviewRankSel.addEventListener("change", updateFromOverviewRankTier);
    overviewTierSel.addEventListener("change", updateFromOverviewRankTier);
    
    const updateFromRankTierXp = () => {
        const rank = parseInt(rankSel.value);
        const tier = parseInt(tierSel.value);
        let xp = parseInt(xpInput.value) || 0;
        
        const xpLimit = getXPForTier(rank);
        if (xp > xpLimit) {
            xp = xpLimit;
            xpInput.value = xpLimit;
        }
        
        const total = rankTierXpToTotalXp(rank, tier, xp);
        totalXpInput.value = total;
        updateRankGauge();
    };
    
    rankSel.addEventListener("change", updateFromRankTierXp);
    tierSel.addEventListener("change", updateFromRankTierXp);
    xpInput.addEventListener("input", updateFromRankTierXp);
    
    totalXpInput.addEventListener("input", () => {
        const totalXp = parseInt(totalXpInput.value) || 0;
        const { rank, tier, xp } = getRankAndTierFromTotalXP(totalXp);
        
        rankSel.value = rank;
        tierSel.value = tier;
        xpInput.value = xp;
        
        updateRankGauge();
    });
    
    // Variables search filter
    document.getElementById("search-variables").addEventListener("input", renderVariablesTable);
    
    // Player Selector change
    document.getElementById("player-selector").addEventListener("change", (e) => {
        saveActiveInventoryToMemory(currentActivePlayerCode);
        saveActiveCoordinatesToMemory(currentActivePlayerCode);
        currentActivePlayerCode = e.target.value;
        document.getElementById("inventory-player-selector").value = e.target.value;
        populateInventoryTab(e.target.value);
        populateFinancesTab();
        loadPlayerDetails(e.target.value);
        if (activeVarType === 'player') {
            renderVariablesTable();
        }
    });
    
    // Inventory Player Selector change
    document.getElementById("inventory-player-selector").addEventListener("change", (e) => {
        saveActiveInventoryToMemory(currentActivePlayerCode);
        saveActiveCoordinatesToMemory(currentActivePlayerCode);
        currentActivePlayerCode = e.target.value;
        document.getElementById("player-selector").value = e.target.value;
        loadPlayerDetails(e.target.value);
        populateInventoryTab(e.target.value);
        populateFinancesTab();
    });

    // Cash on Hand input listener
    document.getElementById("input-cash-on-hand").addEventListener("input", (e) => {
        const newVal = parseFloat(e.target.value) || 0.0;
        const selector = document.getElementById("player-selector");
        const activePlayerCode = selector ? selector.value : "Player_0";
        const player = activeSlot.players.find(p => p.code === activePlayerCode);
        
        if (player && player.inventory && player.inventory.Items) {
            const items = player.inventory.Items;
            let cashSlotFound = false;
            
            for (let i = 0; i < items.length; i++) {
                try {
                    const itemObj = JSON.parse(items[i]);
                    if (itemObj.ID === "cash" || itemObj.DataType === "CashData") {
                        itemObj.CashBalance = newVal;
                        items[i] = JSON.stringify(itemObj);
                        cashSlotFound = true;
                        break;
                    }
                } catch(e) {}
            }
            
            if (!cashSlotFound) {
                let targetSlot = 8;
                for (let i = 0; i < 9; i++) {
                    try {
                        const itemObj = JSON.parse(items[i] || "{}");
                        if (!itemObj.ID) {
                            targetSlot = i;
                            break;
                        }
                    } catch(e) {}
                }
                const cashObj = { DataType: "CashData", DataVersion: 0, GameVersion: activeSlot.metadata.GameVersion || "0.4.5f2", ID: "cash", Quantity: 1, CashBalance: newVal };
                items[targetSlot] = JSON.stringify(cashObj);
            }
            
            const activeInvCode = document.getElementById("inventory-player-selector").value;
            if (activeInvCode === activePlayerCode) {
                populateInventoryTab(activePlayerCode);
            }
        }
    });
    
    // Create manual backup
    document.getElementById("btn-create-backup").addEventListener("click", async () => {
        try {
            const res = await fetch(`/api/backup?slot=${selectedSlotName}&steam_id=${selectedSteamId}`, { method: "POST" });
            const result = await res.json();
            if (result.success) {
                showModal("Success", `Created backup: ${result.backup}`);
                loadBackups();
            } else {
                showModal("Error", result.error || "Backup failed");
            }
        } catch (e) {
            showModal("Error", e.message);
        }
    });

    // Save changes
    document.getElementById("btn-save").addEventListener("click", saveChangesToServer);
    
    // Modal buttons
    document.getElementById("modal-btn-ok").addEventListener("click", () => {
        document.getElementById("alert-modal").classList.add("hidden");
    });
}

// Financial Adjustments helpers
window.adjustFinance = function(inputId, amount) {
    const input = document.getElementById(inputId);
    if (input) {
        let current = parseFloat(input.value) || 0.0;
        input.value = (current + amount).toFixed(2);
    }
};

window.setFinanceMax = function(inputId) {
    const input = document.getElementById(inputId);
    if (input) {
        input.value = "999999999.00";
    }
};

// Teleport preset helper
window.teleportTo = function(x, y, z, rot) {
    document.getElementById("coord-x").value = x.toFixed(4);
    document.getElementById("coord-y").value = y.toFixed(4);
    document.getElementById("coord-z").value = z.toFixed(4);
    document.getElementById("coord-rot").value = rot.toFixed(4);
};

// API: Save changes
async function saveChangesToServer() {
    if (!activeSlot) return;
    
    // Harvest values from UI and inject back to activeSlot object
    saveActiveInventoryToMemory(currentActivePlayerCode);
    saveActiveStorageCratesToMemory();
    saveActiveCoordinatesToMemory(currentActivePlayerCode);
    
    // Rank
    activeSlot.rank.Rank = parseInt(document.getElementById("rank-selector").value);
    activeSlot.rank.Tier = parseInt(document.getElementById("tier-selector").value);
    activeSlot.rank.XP = parseInt(document.getElementById("input-xp").value) || 0;
    activeSlot.rank.TotalXP = parseInt(document.getElementById("input-total-xp").value) || 0;
    
    // Map Regions Unlocked
    const unlocked = [];
    const checkboxes = document.querySelectorAll("#regions-checkbox-container input[type='checkbox']");
    checkboxes.forEach(cb => {
        if (cb.checked) {
            unlocked.push(parseInt(cb.getAttribute("data-region")));
        }
    });
    activeSlot.rank.UnlockedRegions = unlocked;
    
    // Money
    activeSlot.money.OnlineBalance = parseFloat(document.getElementById("input-online-balance").value) || 0.0;
    activeSlot.money.Networth = parseFloat(document.getElementById("input-networth").value) || 0.0;
    activeSlot.money.LifetimeEarnings = parseFloat(document.getElementById("input-lifetime-earnings").value) || 0.0;
    activeSlot.money.WeeklyDepositSum = parseFloat(document.getElementById("input-weekly-deposit").value) || 0.0;
    
    // Post to Server
    try {
        const res = await fetch(`/api/save?slot=${selectedSlotName}&steam_id=${selectedSteamId}`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(activeSlot)
        });
        const result = await res.json();
        if (result.success) {
            showModal("Success", "Save written successfully! A automatic backup was created before saving.");
            populateOverviewTab(); // update home display values
            loadBackups(); // reload backups list
            loadSlots(); // reload sidebar listings
        } else {
            showModal("Error", result.error || "Failed to save.");
        }
    } catch (e) {
        showModal("Error", e.message);
    }
}

// API: Load backups table
async function loadBackups() {
    const tbody = document.getElementById("backups-table-body");
    tbody.innerHTML = "";
    
    try {
        const res = await fetch(`/api/backups?slot=${selectedSlotName}&steam_id=${selectedSteamId}`);
        const backups = await res.json();
        
        if (backups.length === 0) {
            tbody.innerHTML = '<tr><td colspan="3" style="text-align:center; color:var(--text-muted)">No backups created yet for this slot.</td></tr>';
            return;
        }
        
        backups.forEach(b => {
            // format backup name backup_YYYYMMDD_HHMMSS
            const parts = b.split("_");
            let dateStr = "Unknown Date";
            if (parts.length >= 3) {
                const d = parts[1]; // YYYYMMDD
                const t = parts[2]; // HHMMSS
                dateStr = `${d.substring(4,6)}/${d.substring(6,8)}/${d.substring(0,4)} at ${t.substring(0,2)}:${t.substring(2,4)}:${t.substring(4,6)}`;
            }
            
            const tr = document.createElement("tr");
            tr.innerHTML = `
                <td><strong style="color:var(--text-primary)">${b}</strong></td>
                <td>${dateStr}</td>
                <td style="text-align: right;"><button class="btn btn-mini btn-secondary" onclick="restoreBackup('${b}')">Restore</button></td>
            `;
            tbody.appendChild(tr);
        });
    } catch (e) {
        tbody.innerHTML = '<tr><td colspan="3" style="text-align:center; color:var(--color-danger)">Failed to fetch backups.</td></tr>';
    }
}

window.restoreBackup = async function(backupName) {
    if (!confirm(`Are you sure you want to restore the backup: ${backupName}? This will overwrite your current save files!`)) {
        return;
    }
    
    try {
        const res = await fetch(`/api/restore?slot=${selectedSlotName}&steam_id=${selectedSteamId}&backup=${backupName}`, { method: "POST" });
        const result = await res.json();
        if (result.success) {
            showModal("Success", "Backup restored successfully!");
            // Reload the slot data
            selectSaveSlot(selectedSlotName, selectedSteamId);
        } else {
            showModal("Error", result.error || "Failed to restore backup.");
        }
    } catch(e) {
        showModal("Error", e.message);
    }
};

// Modal trigger helper
function showModal(title, message) {
    document.getElementById("modal-title").innerText = title;
    document.getElementById("modal-message").innerText = message;
    document.getElementById("alert-modal").classList.remove("hidden");
}
