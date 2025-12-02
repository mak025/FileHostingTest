(() => {
  const KEY = 'fileViewMode';
  const wrapper = document.getElementById('filesArea');

  const listBtn = document.getElementById('view-list');
  const gridBtn = document.getElementById('view-grid');

  function apply(mode, persist = true) {
    if (wrapper) {
      wrapper.classList.remove('view-list', 'view-grid');
      wrapper.classList.add(mode === 'grid' ? 'view-grid' : 'view-list');
    }

    if (listBtn) listBtn.setAttribute('aria-pressed', mode === 'list' ? 'true' : 'false');
    if (gridBtn) gridBtn.setAttribute('aria-pressed', mode === 'grid' ? 'true' : 'false');

    if (listBtn) listBtn.classList.toggle('active', mode === 'list');
    if (gridBtn) gridBtn.classList.toggle('active', mode === 'grid');

    if (persist) {
      try { localStorage.setItem(KEY, mode); } catch (e) {}
    }
  }

  if (listBtn) listBtn.addEventListener('click', () => apply('list'));
  if (gridBtn) gridBtn.addEventListener('click', () => apply('grid'));

  // modal elements
  const browseButton = document.getElementById('browseButton');
  const modalFileInput = document.getElementById('modalFileInput');
  const dropFileName = document.getElementById('dropFileName');
  const dropzone = document.getElementById('modalDropzone');
  const modalPathInput = document.getElementById('modalPathInput');
  const modalUploadButton = document.getElementById('modalUploadButton');
  const uploadProgress = document.getElementById('uploadProgress');
  const progressBar = uploadProgress ? uploadProgress.querySelector('.progress-bar') : null;
  const uploadForm = document.getElementById('uploadForm');

  if (browseButton && modalFileInput) {
    browseButton.addEventListener('click', () => modalFileInput.click());
  }

  if (modalFileInput && dropFileName) {
    modalFileInput.addEventListener('change', (e) => {
      const f = modalFileInput.files && modalFileInput.files[0];
      dropFileName.textContent = f ? f.name : '';
    });
  }

  // When the modal opens, copy the current path from the page (if any) into the modal hidden input
  var uploadModal = document.getElementById('uploadModal');
  if (uploadModal && modalPathInput) {
    uploadModal.addEventListener('show.bs.modal', function (e) {
      // read path from breadcrumb link or URL - simplest is to read window.location.search
      const params = new URLSearchParams(window.location.search);
      const path = params.get('path') || '';
      modalPathInput.value = path;
    });
  }

  // drag and drop
  if (dropzone && modalFileInput && dropFileName) {
    const prevent = (e) => { e.preventDefault(); e.stopPropagation(); };

    ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(evt => {
      dropzone.addEventListener(evt, prevent, false);
    });

    dropzone.addEventListener('dragover', (e) => {
      dropzone.classList.add('dragover');
    });
    dropzone.addEventListener('dragleave', (e) => {
      dropzone.classList.remove('dragover');
    });
    dropzone.addEventListener('drop', (e) => {
      dropzone.classList.remove('dragover');
      const files = e.dataTransfer.files;
      if (files && files.length) {
        modalFileInput.files = files; // set file input
        dropFileName.textContent = files[0].name;
      }
    });
  }

  // Simple toast helpers
  function showToast(message, isSuccess = true) {
    // create toast container if missing
    let container = document.getElementById('toastContainer');
    if (!container) {
      container = document.createElement('div');
      container.id = 'toastContainer';
      container.style.position = 'fixed';
      container.style.right = '16px';
      container.style.top = '16px';
      container.style.zIndex = 2000;
      document.body.appendChild(container);
    }

    const toast = document.createElement('div');
    toast.className = 'toast align-items-center text-white bg-' + (isSuccess ? 'success' : 'danger') + ' border-0';
    toast.role = 'status';
    toast.ariaLive = 'polite';
    toast.ariaAtomic = 'true';

    toast.innerHTML = '<div class="d-flex"><div class="toast-body">' +
      message + '</div><button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button></div>';

    container.appendChild(toast);
    const bsToast = new bootstrap.Toast(toast, { delay: 4000 });
    bsToast.show();
    toast.addEventListener('hidden.bs.toast', () => toast.remove());
  }

  // Helper to retrieve antiforgery token value from a hidden input if present
  function getAntiForgeryToken() {
    const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    return tokenInput ? tokenInput.value : null;
  }

  // AJAX: create folder
  const createFolderForm = document.querySelector('form[asp-page-handler="CreateFolder"]');
  if (createFolderForm) {
    createFolderForm.addEventListener('submit', function (e) {
      e.preventDefault();
      const formData = new FormData(createFolderForm);
      fetch(createFolderForm.action, {
        method: 'POST',
        body: formData,
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
      }).then(r => r.json()).then(json => {
        if (json && json.success) {
          showToast('Folder created', true);
          // reload to update UI
          window.location.reload();
        } else {
          showToast(json && json.message ? json.message : 'Failed to create folder', false);
        }
      }).catch(() => showToast('Failed to create folder', false));
    });
  }

  // AJAX: delete folder and delete object - hook buttons by data attributes
  document.addEventListener('click', function (e) {
    const delFolder = e.target.closest('[data-delete-folder]');
    if (delFolder) {
      const prefix = delFolder.getAttribute('data-delete-folder');
      if (!confirm('Delete folder and its contents?')) return;
      const token = getAntiForgeryToken();
      const params = new URLSearchParams({ folder: prefix });
      if (token) params.append('__RequestVerificationToken', token);
      fetch('/Index?handler=DeleteFolder', { method: 'POST', body: params })
        .then(r => r.json()).then(json => {
          if (json && json.success) {
            showToast('Folder deleted', true);
            window.location.reload();
          } else {
            showToast('Failed to delete folder', false);
          }
        }).catch(() => showToast('Failed to delete folder', false));
    }

    const delObject = e.target.closest('[data-delete-object]');
    if (delObject) {
      const name = delObject.getAttribute('data-delete-object');
      if (!confirm('Delete file?')) return;
      const token = getAntiForgeryToken();
      const params = new URLSearchParams({ objectName: name });
      if (token) params.append('__RequestVerificationToken', token);
      fetch('/Index?handler=DeleteObject', { method: 'POST', body: params })
        .then(r => r.json()).then(json => {
          if (json && json.success) {
            showToast('File deleted', true);
            window.location.reload();
          } else {
            showToast('Failed to delete file', false);
          }
        }).catch(() => showToast('Failed to delete file', false));
    }
  });

  // AJAX upload with progress
  if (modalUploadButton && uploadForm) {
    modalUploadButton.addEventListener('click', function () {
      // Use the existing form so hidden antiforgery token is included
      const formElement = uploadForm;
      const formData = new FormData(formElement);
      const fileList = formData.getAll('Upload');
      if (!fileList || fileList.length === 0) {
        showToast('Please select a file', false);
        return;
      }

      // Use XMLHttpRequest for progress events
      const xhr = new XMLHttpRequest();
      xhr.open('POST', '/Index?handler=UploadAjax');
      xhr.setRequestHeader('X-Requested-With', 'XMLHttpRequest');

      xhr.upload.addEventListener('progress', function (e) {
        if (!uploadProgress) return;
        uploadProgress.style.display = 'block';
        const pct = e.lengthComputable ? Math.round((e.loaded / e.total) * 100) : 0;
        if (progressBar) progressBar.style.width = pct + '%';
      });

      xhr.addEventListener('load', function () {
        if (uploadProgress) uploadProgress.style.display = 'none';
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const json = JSON.parse(xhr.responseText);
            if (json && json.success) {
              showToast('Upload successful', true);
              window.location.reload();
            } else {
              showToast(json && json.message ? json.message : 'Upload failed', false);
            }
          } catch {
            showToast('Upload failed', false);
          }
        } else {
          showToast('Upload failed', false);
        }
      });

      xhr.addEventListener('error', function () {
        if (uploadProgress) uploadProgress.style.display = 'none';
        showToast('Upload failed', false);
      });

      xhr.send(formData);
    });
  }

  // share modal logic
  document.addEventListener('click', function (e) {
    const shareBtn = e.target.closest('[data-share-object]');
    if (shareBtn) {
      const name = shareBtn.getAttribute('data-share-object');
      // open modal and populate
      const shareModalEl = document.getElementById('shareModal');
      const bm = new bootstrap.Modal(shareModalEl);
      document.getElementById('shareLink').value = '';
      document.getElementById('shareAdvanced').checked = false;
      document.getElementById('advancedOptions').style.display = 'none';
      bm.show();

      // attach click handler for generate
      const genBtn = document.getElementById('generateShareLink');
      genBtn.onclick = function () {
        const adv = document.getElementById('shareAdvanced').checked;
        let secs = 3600 * 12; // default 12 hours
        if (adv) {
          const d = parseInt(document.getElementById('shareDays').value || '0', 10);
          const h = parseInt(document.getElementById('shareHours').value || '0', 10);
          const m = parseInt(document.getElementById('shareMinutes').value || '0', 10);
          secs = d * 86400 + h * 3600 + m * 60;
        }

        // Build form-encoded params and include antiforgery token so Razor Pages will accept the POST
        const params = new URLSearchParams({ objectName: name, expires: secs });
        const token = getAntiForgeryToken();
        if (token) params.append('__RequestVerificationToken', token);

        fetch('/Index?handler=Presign', {
          method: 'POST',
          body: params,
          headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
          .then(async (r) => {
            // Try to parse JSON, but handle non-JSON responses gracefully
            const text = await r.text();
            try { return JSON.parse(text); } catch { return { success: false, message: text }; }
          })
          .then(json => {
            if (json && json.url) {
              // open the SharedDownload link so browser will download via server proxy
              window.open(json.url, '_blank');
              // also populate the shareLink input with the same URL
              document.getElementById('shareLink').value = json.url;
            } else {
              document.getElementById('shareLink').value = json && json.message ? json.message : 'Failed to generate link';
            }
          }).catch(() => document.getElementById('shareLink').value = 'Failed to generate link');
      };

      // toggle advanced UI
      document.getElementById('shareAdvanced').onchange = function () {
        document.getElementById('advancedOptions').style.display = this.checked ? 'block' : 'none';
      };
    }
  });

  const saved = (function () { try { return localStorage.getItem(KEY); } catch (e) { return null; } })();
  apply(saved === 'grid' ? 'grid' : 'list', false);
})();