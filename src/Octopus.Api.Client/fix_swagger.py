import json
import os

# Change to the script's directory
os.chdir(os.path.dirname(os.path.abspath(__file__)))

with open('swagger.json.new', 'r') as f:
    spec = json.load(f)

count = 0
# Process each path
for path, methods in spec.get('paths', {}).items():
    for method, details in methods.items():
        if isinstance(details, dict) and 'responses' in details:
            responses = details['responses']
            # If there's a 201 response, remove any 200 response with empty content
            if '201' in responses and '200' in responses:
                r200 = responses['200']
                if isinstance(r200, dict):
                    content = r200.get('content', {})
                    # Check if content is empty or has empty application/json
                    if not content or (isinstance(content, dict) and
                        'application/json' in content and
                        not content['application/json']):
                        del responses['200']
                        count += 1
                        print(f"Removed empty 200 from {method.upper()} {path}")

with open('swagger.json', 'w') as f:
    json.dump(spec, f, indent=2)
print(f"Fixed {count} endpoints. Saved to swagger.json")
