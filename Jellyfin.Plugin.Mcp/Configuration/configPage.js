// Controller for the MCP Server dashboard page. Jellyfin loads it through the page's data-controller
// attribute (the pattern used by jellyfin-plugin-webhook). All user-controlled strings are rendered
// with textContent, and secrets are never written to the console or to browser storage.
const PLUGIN_ID = '08dd6fbf-c026-48c9-9405-824c3906df09';
const TOKENS_PATH = 'McpServer/Tokens';

export default function (view) {
    const $ = (id) => view.querySelector('#' + id);

    let users = [];

    function endpointUrl() {
        return ApiClient.serverAddress().replace(/\/+$/, '') + '/mcp';
    }

    function formatDate(value) {
        return value ? new Date(value).toLocaleString() : '-';
    }

    function cell(row, text) {
        const td = document.createElement('td');
        td.textContent = text;
        row.appendChild(td);
        return td;
    }

    function legacyCopy() {
        try {
            return document.execCommand('copy');
        } catch (e) {
            return false;
        }
    }

    // Copies the text of a source field. The clipboard API needs a secure context, which plain-HTTP LAN
    // installs lack, so fall back to execCommand on the source field itself (it lives inside the modal
    // dialog, where elements outside the dialog would be inert), then on a temporary field in the same parent.
    async function copyFrom(source) {
        const text = source.value;
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return;
            }
        } catch (e) {
            // fall through to the legacy paths
        }

        source.select();
        if (legacyCopy()) {
            return;
        }

        const area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        source.parentElement.appendChild(area);
        area.select();
        const ok = legacyCopy();
        area.remove();
        if (!ok) {
            source.select();
            toast('Copy failed. Select the text and copy it manually.');
        }
    }

    function toast(message) {
        Dashboard.alert(message);
    }

    function tokenRequest(type, url, body) {
        const options = { type: type, url: ApiClient.getUrl(url) };
        if (body !== undefined) {
            options.data = JSON.stringify(body);
            options.contentType = 'application/json';
        }

        if (type !== 'DELETE') {
            options.dataType = 'json';
        }

        return ApiClient.ajax(options);
    }

    function renderTokens(tokens) {
        const body = $('mcpTokenRows');
        body.textContent = '';
        $('mcpNoTokens').hidden = tokens.length > 0;

        tokens.forEach((token) => {
            const row = document.createElement('tr');
            cell(row, token.Name);
            cell(row, token.UserName || '(deleted user)');
            cell(row, (token.Scopes || []).join(', '));
            const libraries = token.Libraries || [];
            cell(row, libraries.length === 0 ? 'all' : libraries.map((l) => l.Name || '(deleted library)').join(', '));
            cell(row, token.Prefix + '...');
            cell(row, formatDate(token.CreatedAt));
            cell(row, formatDate(token.LastUsedAt));
            cell(row, token.ExpiresAt ? formatDate(token.ExpiresAt) : 'never');
            cell(row, token.Enabled ? 'yes' : 'no');

            const actions = document.createElement('td');
            const revoke = document.createElement('button');
            revoke.type = 'button';
            revoke.className = 'raised button emby-button';
            revoke.textContent = 'Revoke';
            revoke.addEventListener('click', () => revokeToken(token));
            actions.appendChild(revoke);
            row.appendChild(actions);
            body.appendChild(row);
        });
    }

    async function loadTokens() {
        renderTokens(await tokenRequest('GET', TOKENS_PATH));
    }

    async function revokeToken(token) {
        // window.confirm renders plain text only, so the token name cannot inject markup.
        if (!window.confirm('Revoke token "' + token.Name + '"? Clients using it will stop working immediately.')) {
            return;
        }

        Dashboard.showLoadingMsg();
        try {
            await tokenRequest('DELETE', TOKENS_PATH + '/' + encodeURIComponent(token.Id));
            await loadTokens();
        } catch (e) {
            toast('Could not revoke the token.');
        } finally {
            Dashboard.hideLoadingMsg();
        }
    }

    function selectedLibraries() {
        return Array.from(view.querySelectorAll('#mcpLibraries input[type=checkbox]'))
            .filter((box) => box.checked)
            .map((box) => box.dataset.library);
    }

    // The admin scope needs an administrator and cannot be combined with a library restriction
    // (admin tools act on the whole server); the server rejects both cases too.
    function updateAdminScope() {
        const user = users.find((u) => u.Id === $('mcpTokenUser').value);
        const isAdmin = !!(user && user.Policy && user.Policy.IsAdministrator);
        const allowed = isAdmin && selectedLibraries().length === 0;
        const box = $('mcpScopeAdmin');
        box.disabled = !allowed;
        if (!allowed) {
            box.checked = false;
        }
    }

    function renderLibraries(libraries) {
        const list = $('mcpLibraries');
        list.textContent = '';
        $('mcpNoLibraries').hidden = libraries.length > 0;
        libraries.forEach((library) => {
            const container = document.createElement('div');
            container.className = 'checkboxContainer';
            const label = document.createElement('label');
            label.className = 'emby-checkbox-label';
            // Static markup only: Jellyfin wraps document.createElement and rejects the options argument,
            // so the emby-checkbox is created by the parser and upgraded when it is attached.
            label.innerHTML = '<input type="checkbox" is="emby-checkbox" /><span class="checkboxLabel"></span>';
            const box = label.querySelector('input');
            box.dataset.library = library.Id;
            box.addEventListener('change', updateAdminScope);
            label.querySelector('span').textContent = library.CollectionType ? library.Name + ' (' + library.CollectionType + ')' : library.Name;
            container.appendChild(label);
            list.appendChild(container);
        });
        updateAdminScope();
    }

    async function loadLibraries() {
        const userId = $('mcpTokenUser').value;
        renderLibraries([]);
        $('mcpNoLibraries').hidden = true;
        if (!userId) {
            return;
        }

        try {
            const libraries = await tokenRequest('GET', TOKENS_PATH + '/Libraries?userId=' + encodeURIComponent(userId));
            if ($('mcpTokenUser').value === userId) {
                renderLibraries(libraries);
            }
        } catch (e) {
            toast('Could not load the libraries of this user.');
        }
    }

    function onUserChange() {
        updateAdminScope();
        return loadLibraries();
    }

    function fillUsers() {
        const select = $('mcpTokenUser');
        select.textContent = '';
        users.forEach((user) => {
            const option = document.createElement('option');
            option.value = user.Id;
            option.textContent = user.Name;
            select.appendChild(option);
        });
        return onUserChange();
    }

    function clearTokenDialog() {
        $('mcpDlgToken').value = '';
        $('mcpDlgCode').value = '';
        $('mcpDlgDesktop').value = '';
    }

    function showTokenDialog(plaintext) {
        const url = endpointUrl();
        const header = 'Authorization: Bearer ' + plaintext;
        $('mcpDlgToken').value = plaintext;
        $('mcpDlgCode').value = 'claude mcp add --transport http jellyfin ' + url + ' --header "' + header + '"';
        $('mcpDlgDesktop').value = JSON.stringify({
            mcpServers: {
                jellyfin: {
                    command: 'npx',
                    args: ['-y', 'mcp-remote', url, '--header', header]
                }
            }
        });
        $('mcpTokenDialog').showModal();
    }

    async function createToken(event) {
        event.preventDefault();
        const scopes = Array.from(view.querySelectorAll('#mcpScopes input[type=checkbox]'))
            .filter((box) => box.checked && !box.disabled)
            .map((box) => box.dataset.scope);
        if (scopes.length === 0) {
            toast('Select at least one scope.');
            return;
        }

        const libraryIds = selectedLibraries();
        const expiry = $('mcpTokenExpiry').value;
        Dashboard.showLoadingMsg();
        try {
            const created = await tokenRequest('POST', TOKENS_PATH, {
                Name: $('mcpTokenName').value.trim(),
                UserId: $('mcpTokenUser').value,
                Scopes: scopes,
                LibraryIds: libraryIds,
                ExpiresInDays: expiry === '' ? null : parseInt(expiry, 10)
            });
            $('mcpTokenName').value = '';
            showTokenDialog(created.Plaintext);
        } catch (e) {
            toast('Could not create the token. Check the name, user, scopes and libraries.');
            return;
        } finally {
            Dashboard.hideLoadingMsg();
        }

        try {
            await loadTokens();
        } catch (e) {
            toast('Token created, but the list could not be refreshed.');
        }
    }

    async function saveSettings(event) {
        event.preventDefault();
        Dashboard.showLoadingMsg();
        try {
            // Fetch the current configuration and change only the settings fields. Tokens are managed
            // exclusively through McpServer/Tokens and are never sent back (the server ignores them anyway).
            const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
            delete config.Tokens;
            config.Enabled = $('mcpEnabled').checked;
            config.MaxPageSize = Math.min(1000, Math.max(1, parseInt($('mcpMaxPageSize').value, 10) || 100));
            config.LogToolCalls = $('mcpLogToolCalls').checked;
            config.AllowedOrigins = $('mcpAllowedOrigins').value.split('\n').map((o) => o.trim()).filter((o) => o.length > 0);
            Dashboard.processPluginConfigurationUpdateResult(await ApiClient.updatePluginConfiguration(PLUGIN_ID, config));
        } catch (e) {
            Dashboard.hideLoadingMsg();
            toast('Could not save the settings.');
        }
    }

    async function load() {
        Dashboard.showLoadingMsg();
        try {
            $('mcpEndpointUrl').value = endpointUrl();
            const [config, userList] = await Promise.all([
                ApiClient.getPluginConfiguration(PLUGIN_ID),
                ApiClient.getUsers()
            ]);
            $('mcpEnabled').checked = config.Enabled;
            $('mcpMaxPageSize').value = config.MaxPageSize;
            $('mcpLogToolCalls').checked = config.LogToolCalls;
            $('mcpAllowedOrigins').value = (config.AllowedOrigins || []).join('\n');
            users = userList;
            await fillUsers();
            await loadTokens();
        } catch (e) {
            toast('Could not load the MCP Server configuration.');
        } finally {
            Dashboard.hideLoadingMsg();
        }
    }

    $('mcpSettingsForm').addEventListener('submit', saveSettings);
    $('mcpTokenForm').addEventListener('submit', createToken);
    $('mcpTokenUser').addEventListener('change', onUserChange);
    $('mcpCopyUrl').addEventListener('click', () => copyFrom($('mcpEndpointUrl')));
    $('mcpCopyToken').addEventListener('click', () => copyFrom($('mcpDlgToken')));
    $('mcpCopyCode').addEventListener('click', () => copyFrom($('mcpDlgCode')));
    $('mcpCopyDesktop').addEventListener('click', () => copyFrom($('mcpDlgDesktop')));
    $('mcpCloseDialog').addEventListener('click', () => {
        clearTokenDialog();
        $('mcpTokenDialog').close();
    });
    $('mcpTokenDialog').addEventListener('cancel', clearTokenDialog);
    view.addEventListener('viewshow', load);
}
