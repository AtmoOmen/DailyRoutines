import os
import requests

def get_total_downloads(user_name, repo_name):
    total_downloads = 0
    page = 1
    per_page = 100
    while True:
        url = f"https://api.github.com/repos/{user_name}/{repo_name}/releases?per_page={per_page}&page={page}"
        headers = {
            'User-Agent': 'request',
            'Authorization': f'token {os.environ["GITHUB_TOKEN"]}'
        }
        response = requests.get(url, headers=headers)
        if response.status_code != 200:
            break
        releases = response.json()
        if not releases:
            break
        for release in releases:
            for asset in release['assets']:
                total_downloads += asset['download_count'] * 2
        page += 1
    return total_downloads + 171320

user_name = 'AtmoOmen'
repo_name = 'DailyRoutines'
total_downloads = get_total_downloads(user_name, repo_name)
print(f"{total_downloads}")
