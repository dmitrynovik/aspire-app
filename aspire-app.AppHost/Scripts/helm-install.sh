NS="test"
CHART="aspire-app"
VERSION="0.1.0"
ENVIRONMENT="Development"
ACR_PREFIX="aspireapp.io/"
ACR="aspireapp.azurecr.io"
ACR_NAME="aspireapp.azurecr.io"

az acr login --name $ACR_NAME

SECRETS_FILE=$(realpath "secrets.yaml")
echo $SECRETS_FILE

#aspire publish --clear-cache
aspire deploy --clear-cache

pushd ../
pushd "aspire-output"
pushd mi-k8s

# Directory to search for YAML files ('.' means current directory)
SEARCH_DIR="."

# Find all .yaml files recursively and remove quotes around port values
# Issue https://github.com/dotnet/aspire/issues/11789
find "$SEARCH_DIR" -type f \( -name 'service.yaml' -o -name 'deployment.yaml' \) -print0 |
while IFS= read -r -d $'\0' file; do
    echo "Processing file: $file"
    # Use sed to modify the file in place (-i)
    # For each line containing 'port:', remove all double quotes (")
    # This targets the value part, e.g., '{{ .Values.param }}'
    sed -i '/port:/s/"//g' "$file"
    sed -i '/containerPort:/s/"//g' "$file"
done

# AMEND ACR REGISTRY
#yq -i "(.. | select(has(\"image\")).image) |= \"$ACR_PREFIX\" + ." values.yaml
#sed -i "/mienvacrzbqryxzh6p5lu\.azurecr\.io/! s|^\([[:space:]]*[a-zA-Z_]*image:[[:space:]]*[\"']\?\)|\1${ACR_PREFIX}|" values.yaml
# REMOVE UNUSED SERVICES__ FROM GATEWAY CONFIG:
#sed -i "/^[[:space:]]*services__/d" "./templates/gateway/config.yaml"
echo "Done processing all YAML files."

helm package .

kubectl create namespace $NS --dry-run=client -o yaml | kubectl apply -f -

# TODO: parameterize
# az aks update --name mi-k8s --resource-group rg-devtest-mandalay-integration --attach-acr mienvacrzbqryxzh6p5lu

pwd
helm -n $NS upgrade --install $CHART ./$CHART-$VERSION.tgz -f $SECRETS_FILE -f values.yaml --debug

popd
popd
popd