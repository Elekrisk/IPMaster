include .env
export

all:
	dotnet build

release:
	dotnet build -c Release

publish: release
	dotnet tcli publish --config-path Thunderstore/thunderstore.toml --file $$(find Thunderstore/Packages -type f -iname "*.zip" | sort -V | tail -n1)

run: all
	slc