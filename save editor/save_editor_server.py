import os
import json
import shutil
import datetime
import webbrowser
import http.server
import socketserver
import urllib.parse

# Path configuration
USER_PROFILE = os.environ.get('USERPROFILE') or os.path.expanduser('~')
SAVES_BASE = os.path.join(USER_PROFILE, 'AppData', 'LocalLow', 'TVGS', 'Schedule I', 'Saves')

def get_slot_path(steam_id, slot_name):
    return os.path.join(SAVES_BASE, steam_id, slot_name)

def get_backups_root(slot_path):
    return os.path.join(slot_path, 'save_editor_backups')

def find_save_slots():
    slots = []
    if not os.path.exists(SAVES_BASE):
        return slots
    try:
        for entry in os.listdir(SAVES_BASE):
            steam_dir = os.path.join(SAVES_BASE, entry)
            if os.path.isdir(steam_dir):
                # Exclude Unity-related system folders
                if entry.lower() in ['unity', 'logs', 'settings']:
                    continue
                for sub in os.listdir(steam_dir):
                    slot_dir = os.path.join(steam_dir, sub)
                    if os.path.isdir(slot_dir) and sub.startswith("SaveGame_") and not sub.endswith("_Backup"):
                        # Read metadata for preview
                        meta = {}
                        rank = {}
                        money = {}
                        
                        meta_path = os.path.join(slot_dir, 'Metadata.json')
                        if os.path.exists(meta_path):
                            try:
                                with open(meta_path, 'r', encoding='utf-8') as f:
                                    meta = json.load(f)
                            except: pass
                            
                        rank_path = os.path.join(slot_dir, 'Rank.json')
                        if os.path.exists(rank_path):
                            try:
                                with open(rank_path, 'r', encoding='utf-8') as f:
                                    rank = json.load(f)
                            except: pass

                        money_path = os.path.join(slot_dir, 'Money.json')
                        if os.path.exists(money_path):
                            try:
                                with open(money_path, 'r', encoding='utf-8') as f:
                                    money = json.load(f)
                            except: pass
                            
                        slots.append({
                            "name": sub,
                            "steam_id": entry,
                            "path": slot_dir,
                            "last_played": meta.get("LastPlayedDate", {}),
                            "rank": rank.get("Rank", 0),
                            "tier": rank.get("Tier", 1),
                            "online_balance": money.get("OnlineBalance", 0.0),
                            "networth": money.get("Networth", 0.0)
                        })
    except Exception as e:
        print("Error finding save slots:", e)
    return slots

class SaveEditorHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        # Disable caching for APIs
        if self.path.startswith('/api/'):
            self.send_header('Cache-Control', 'no-store, no-cache, must-revalidate, max-age=0')
        super().end_headers()

    def do_GET(self):
        parsed_url = urllib.parse.urlparse(self.path)
        path = parsed_url.path
        query = urllib.parse.parse_qs(parsed_url.query)
        
        # API Routes
        if path == '/api/saves':
            self.send_json_response(find_save_slots())
            return
            
        elif path == '/api/save':
            slot = query.get('slot', [None])[0]
            steam_id = query.get('steam_id', [None])[0]
            if not slot or not steam_id:
                self.send_error_response(400, "Missing 'slot' or 'steam_id' parameters")
                return
                
            slot_path = get_slot_path(steam_id, slot)
            if not os.path.exists(slot_path):
                self.send_error_response(404, f"Save slot {slot} not found")
                return
                
            self.send_json_response(self.load_save_data(slot_path))
            return
            
        elif path == '/api/backups':
            slot = query.get('slot', [None])[0]
            steam_id = query.get('steam_id', [None])[0]
            if not slot or not steam_id:
                self.send_error_response(400, "Missing 'slot' or 'steam_id' parameters")
                return
                
            slot_path = get_slot_path(steam_id, slot)
            backups_dir = get_backups_root(slot_path)
            backups = []
            if os.path.exists(backups_dir):
                for d in sorted(os.listdir(backups_dir), reverse=True):
                    if os.path.isdir(os.path.join(backups_dir, d)) and d.startswith("backup_"):
                        backups.append(d)
            self.send_json_response(backups)
            return

        # Serve static files from the current folder (save editor folder)
        super().do_GET()

    def do_POST(self):
        parsed_url = urllib.parse.urlparse(self.path)
        path = parsed_url.path
        query = urllib.parse.parse_qs(parsed_url.query)
        
        if path == '/api/save':
            slot = query.get('slot', [None])[0]
            steam_id = query.get('steam_id', [None])[0]
            if not slot or not steam_id:
                self.send_error_response(400, "Missing 'slot' or 'steam_id'")
                return
                
            slot_path = get_slot_path(steam_id, slot)
            if not os.path.exists(slot_path):
                self.send_error_response(404, f"Save slot {slot} not found")
                return
                
            content_length = int(self.headers['Content-Length'])
            post_data = self.rfile.read(content_length)
            try:
                save_data = json.loads(post_data.decode('utf-8'))
                self.write_save_data(slot_path, save_data)
                self.send_json_response({"success": True, "message": "Save written successfully!"})
            except Exception as e:
                self.send_error_response(500, f"Error saving data: {str(e)}")
            return
            
        elif path == '/api/backup':
            slot = query.get('slot', [None])[0]
            steam_id = query.get('steam_id', [None])[0]
            if not slot or not steam_id:
                self.send_error_response(400, "Missing 'slot' or 'steam_id'")
                return
                
            slot_path = get_slot_path(steam_id, slot)
            try:
                backup_name = self.create_backup(slot_path)
                self.send_json_response({"success": True, "backup": backup_name})
            except Exception as e:
                self.send_error_response(500, f"Error creating backup: {str(e)}")
            return
            
        elif path == '/api/restore':
            slot = query.get('slot', [None])[0]
            steam_id = query.get('steam_id', [None])[0]
            backup = query.get('backup', [None])[0]
            if not slot or not steam_id or not backup:
                self.send_error_response(400, "Missing 'slot', 'steam_id', or 'backup'")
                return
                
            slot_path = get_slot_path(steam_id, slot)
            try:
                self.restore_backup(slot_path, backup)
                self.send_json_response({"success": True, "message": f"Successfully restored {backup}!"})
            except Exception as e:
                self.send_error_response(500, f"Error restoring backup: {str(e)}")
            return
            
        self.send_error_response(404, "Endpoint not found")

    def send_json_response(self, data):
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        response_bytes = json.dumps(data).encode('utf-8')
        self.send_header('Content-Length', str(len(response_bytes)))
        self.end_headers()
        self.wfile.write(response_bytes)

    def send_error_response(self, code, message):
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        response_bytes = json.dumps({"success": False, "error": message}).encode('utf-8')
        self.send_header('Content-Length', str(len(response_bytes)))
        self.end_headers()
        self.wfile.write(response_bytes)

    def load_save_data(self, slot_path):
        result = {
            "metadata": {},
            "rank": {},
            "money": {},
            "variables": {},
            "players": [],
            "products": {},
            "businesses": [],
            "properties": []
        }
        
        # Load core files
        for key in ["Metadata", "Rank", "Money", "Variables", "Products"]:
            file_path = os.path.join(slot_path, f"{key}.json")
            if os.path.exists(file_path):
                try:
                    with open(file_path, 'r', encoding='utf-8') as f:
                        result[key.lower()] = json.load(f)
                except Exception as e:
                    print(f"Error loading {key}.json:", e)
                    
        # Load players
        players_dir = os.path.join(slot_path, "Players")
        if os.path.exists(players_dir):
            for player_code in os.listdir(players_dir):
                player_path = os.path.join(players_dir, player_code)
                if os.path.isdir(player_path):
                    p_data = {"code": player_code}
                    # Read player json files
                    for key in ["Player", "Variables", "Appearance", "Clothing", "Inventory"]:
                        f_path = os.path.join(player_path, f"{key}.json")
                        if os.path.exists(f_path):
                            try:
                                with open(f_path, 'r', encoding='utf-8') as f:
                                    p_data[key.lower()] = json.load(f)
                            except Exception as e:
                                print(f"Error loading Player {player_code} {key}.json:", e)
                    result["players"].append(p_data)
                    
        # Load businesses
        biz_dir = os.path.join(slot_path, "Businesses")
        if os.path.exists(biz_dir):
            for biz_file in os.listdir(biz_dir):
                if biz_file.endswith(".json"):
                    f_path = os.path.join(biz_dir, biz_file)
                    try:
                        with open(f_path, 'r', encoding='utf-8') as f:
                            result["businesses"].append(json.load(f))
                    except Exception as e:
                        print(f"Error loading Business {biz_file}:", e)

        # Load properties
        prop_dir = os.path.join(slot_path, "Properties")
        if os.path.exists(prop_dir):
            for prop_file in os.listdir(prop_dir):
                if prop_file.endswith(".json"):
                    f_path = os.path.join(prop_dir, prop_file)
                    try:
                        with open(f_path, 'r', encoding='utf-8') as f:
                            result["properties"].append(json.load(f))
                    except Exception as e:
                        print(f"Error loading Property {prop_file}:", e)
                        
        return result

    def create_backup(self, slot_path):
        timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        backup_name = f"backup_{timestamp}"
        backups_dir = get_backups_root(slot_path)
        dest_dir = os.path.join(backups_dir, backup_name)
        
        os.makedirs(dest_dir, exist_ok=True)
        
        # Copy core files
        for key in ["Metadata.json", "Rank.json", "Money.json", "Variables.json", "Products.json"]:
            src = os.path.join(slot_path, key)
            if os.path.exists(src):
                shutil.copy2(src, dest_dir)
                
        # Copy players directory
        src_players = os.path.join(slot_path, "Players")
        if os.path.exists(src_players):
            dest_players = os.path.join(dest_dir, "Players")
            shutil.copytree(src_players, dest_players)

        # Copy businesses directory
        src_biz = os.path.join(slot_path, "Businesses")
        if os.path.exists(src_biz):
            dest_biz = os.path.join(dest_dir, "Businesses")
            shutil.copytree(src_biz, dest_biz)

        # Copy properties directory
        src_prop = os.path.join(slot_path, "Properties")
        if os.path.exists(src_prop):
            dest_prop = os.path.join(dest_dir, "Properties")
            shutil.copytree(src_prop, dest_prop)
            
        print(f"Created backup at: {dest_dir}")
        return backup_name

    def write_save_data(self, slot_path, data):
        # 1. Create a backup first!
        self.create_backup(slot_path)
        
        # 2. Write core JSON files
        # Rank.json
        if "rank" in data:
            with open(os.path.join(slot_path, "Rank.json"), "w", encoding="utf-8") as f:
                json.dump(data["rank"], f, indent=4)
                
        # Money.json
        if "money" in data:
            with open(os.path.join(slot_path, "Money.json"), "w", encoding="utf-8") as f:
                json.dump(data["money"], f, indent=4)
                
        # Variables.json (global)
        if "variables" in data:
            with open(os.path.join(slot_path, "Variables.json"), "w", encoding="utf-8") as f:
                json.dump(data["variables"], f, indent=4)
                
        # Products.json
        if "products" in data:
            with open(os.path.join(slot_path, "Products.json"), "w", encoding="utf-8") as f:
                json.dump(data["products"], f, indent=4)

        # Businesses/
        if "businesses" in data:
            biz_dir = os.path.join(slot_path, "Businesses")
            os.makedirs(biz_dir, exist_ok=True)
            for biz in data["businesses"]:
                code_to_name = {
                    "carwash": "Car Wash",
                    "laundromat": "Laundromat",
                    "postoffice": "Post Office",
                    "tacoticklers": "Taco Ticklers"
                }
                code = biz.get("PropertyCode")
                fname = code_to_name.get(code)
                if not fname:
                    fname = code.capitalize()
                
                with open(os.path.join(biz_dir, f"{fname}.json"), "w", encoding="utf-8") as f:
                    json.dump(biz, f, indent=4)

        # Properties/
        if "properties" in data:
            prop_dir = os.path.join(slot_path, "Properties")
            os.makedirs(prop_dir, exist_ok=True)
            for prop in data["properties"]:
                code_to_name = {
                    "barn": "Barn",
                    "seweroffice": "Sewer Office",
                    "sweatshop": "Sweatshop",
                    "motelroom": "Motel Room",
                    "manor": "Hyland Manor"
                }
                code = prop.get("PropertyCode")
                fname = code_to_name.get(code)
                if not fname:
                    fname = code.capitalize()
                
                with open(os.path.join(prop_dir, f"{fname}.json"), "w", encoding="utf-8") as f:
                    json.dump(prop, f, indent=4)
                
        # 3. Write player files
        if "players" in data:
            for p in data["players"]:
                player_code = p["code"]
                player_dir = os.path.join(slot_path, "Players", player_code)
                os.makedirs(player_dir, exist_ok=True)
                
                # Write each player-scoped file
                if "player" in p:
                    with open(os.path.join(player_dir, "Player.json"), "w", encoding="utf-8") as f:
                        json.dump(p["player"], f, indent=4)
                if "variables" in p:
                    with open(os.path.join(player_dir, "Variables.json"), "w", encoding="utf-8") as f:
                        json.dump(p["variables"], f, indent=4)
                if "appearance" in p:
                    with open(os.path.join(player_dir, "Appearance.json"), "w", encoding="utf-8") as f:
                        json.dump(p["appearance"], f, indent=4)
                if "clothing" in p:
                    with open(os.path.join(player_dir, "Clothing.json"), "w", encoding="utf-8") as f:
                        json.dump(p["clothing"], f, indent=4)
                if "inventory" in p:
                    with open(os.path.join(player_dir, "Inventory.json"), "w", encoding="utf-8") as f:
                        json.dump(p["inventory"], f, indent=4)
                        
        print(f"Successfully saved and updated files in: {slot_path}")

    def restore_backup(self, slot_path, backup_name):
        backups_dir = get_backups_root(slot_path)
        backup_src = os.path.join(backups_dir, backup_name)
        if not os.path.exists(backup_src):
            raise Exception("Backup folder not found")
            
        # Copy core files back
        for key in ["Metadata.json", "Rank.json", "Money.json", "Variables.json", "Products.json"]:
            src = os.path.join(backup_src, key)
            if os.path.exists(src):
                shutil.copy2(src, slot_path)
                
        # Copy players folder back
        src_players = os.path.join(backup_src, "Players")
        dest_players = os.path.join(slot_path, "Players")
        if os.path.exists(src_players):
            if os.path.exists(dest_players):
                shutil.rmtree(dest_players)
            shutil.copytree(src_players, dest_players)
            
        # Copy businesses folder back
        src_biz = os.path.join(backup_src, "Businesses")
        dest_biz = os.path.join(slot_path, "Businesses")
        if os.path.exists(src_biz):
            if os.path.exists(dest_biz):
                shutil.rmtree(dest_biz)
            shutil.copytree(src_biz, dest_biz)
            
        # Copy properties folder back
        src_prop = os.path.join(backup_src, "Properties")
        dest_prop = os.path.join(slot_path, "Properties")
        if os.path.exists(src_prop):
            if os.path.exists(dest_prop):
                shutil.rmtree(dest_prop)
            shutil.copytree(src_prop, dest_prop)
            
        print(f"Restored backup {backup_name} to {slot_path}")

def start_server():
    # Change working directory to the folder containing this server script
    # to serve files correctly.
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    
    port = 8000
    handler = SaveEditorHandler
    
    # Try opening the port, increment if busy
    while port < 8080:
        try:
            with socketserver.TCPServer(("", port), handler) as httpd:
                url = f"http://localhost:{port}"
                print(f"\n==============================================")
                print(f" Schedule I Save Editor Running Locally!")
                print(f" Open your browser to: {url}")
                print(f"==============================================\n")
                
                # Auto-open browser
                webbrowser.open(url)
                
                httpd.serve_forever()
        except OSError:
            print(f"Port {port} in use, trying next...")
            port += 1

if __name__ == "__main__":
    start_server()
