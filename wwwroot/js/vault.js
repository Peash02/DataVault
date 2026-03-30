// ── Background Canvas ──────────────────────────────────────────────────
(function () {
  const canvas = document.getElementById('bgCanvas');
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  let W, H, pts;

  function resize() {
    W = canvas.width = window.innerWidth;
    H = canvas.height = window.innerHeight;
  }

  function initPts() {
    pts = Array.from({ length: 55 }, () => ({
      x: Math.random() * W,
      y: Math.random() * H,
      vx: (Math.random() - 0.5) * 0.25,
      vy: (Math.random() - 0.5) * 0.25,
      r: Math.random() * 1.2 + 0.4,
    }));
  }

  function draw() {
    ctx.clearRect(0, 0, W, H);
    // Dots
    pts.forEach(p => {
      p.x += p.vx; p.y += p.vy;
      if (p.x < 0 || p.x > W) p.vx *= -1;
      if (p.y < 0 || p.y > H) p.vy *= -1;
      ctx.beginPath();
      ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
      ctx.fillStyle = 'rgba(0,255,200,0.18)';
      ctx.fill();
    });
    // Lines
    for (let i = 0; i < pts.length; i++) {
      for (let j = i + 1; j < pts.length; j++) {
        const d = Math.hypot(pts[i].x - pts[j].x, pts[i].y - pts[j].y);
        if (d < 110) {
          ctx.beginPath();
          ctx.moveTo(pts[i].x, pts[i].y);
          ctx.lineTo(pts[j].x, pts[j].y);
          ctx.strokeStyle = `rgba(0,255,200,${0.06 * (1 - d / 110)})`;
          ctx.lineWidth = 0.6;
          ctx.stroke();
        }
      }
    }
    requestAnimationFrame(draw);
  }

  resize(); initPts(); draw();
  window.addEventListener('resize', () => { resize(); initPts(); });
})();

// ── Live Clock ─────────────────────────────────────────────────────────
(function () {
  const el = document.getElementById('liveClock');
  if (!el) return;
  function tick() {
    const now = new Date();
    el.textContent = now.toISOString().slice(0, 19).replace('T', ' ') + ' UTC';
  }
  tick();
  setInterval(tick, 1000);
})();

// ── Modal System ────────────────────────────────────────────────────────
function openModal(id) {
  const el = document.getElementById(id);
  if (el) {
    el.classList.add('open');
    document.body.style.overflow = 'hidden';
  }
}

function closeModal(id) {
  const el = document.getElementById(id);
  if (el) {
    el.classList.remove('open');
    document.body.style.overflow = '';
  }
}

// Close modal on backdrop click
document.addEventListener('click', function (e) {
  if (e.target.classList.contains('modal-backdrop')) {
    e.target.classList.remove('open');
    document.body.style.overflow = '';
  }
});

// Close on Escape
document.addEventListener('keydown', function (e) {
  if (e.key === 'Escape') {
    document.querySelectorAll('.modal-backdrop.open').forEach(m => {
      m.classList.remove('open');
      document.body.style.overflow = '';
    });
  }
});

// ── Copy Share URL (from alert bar) ────────────────────────────────────
function copyShareUrl() {
  const text = document.getElementById('shareUrlText')?.textContent;
  if (text) {
    navigator.clipboard.writeText(text).then(() => {
      const btn = event.target;
      btn.textContent = '✓ COPIED';
      btn.style.background = 'rgba(6,214,160,0.2)';
      btn.style.color = '#06d6a0';
      setTimeout(() => {
        btn.textContent = 'COPY';
        btn.style.background = '';
        btn.style.color = '';
      }, 2500);
    });
  }
}

// ── Upload form progress feedback ──────────────────────────────────────
document.addEventListener('DOMContentLoaded', function () {
  // Auto-dismiss alerts after 6 seconds
  document.querySelectorAll('.alert').forEach(a => {
    setTimeout(() => {
      a.style.transition = 'opacity 0.5s';
      a.style.opacity = '0';
      setTimeout(() => a.remove(), 500);
    }, 6000);
  });
});

// ── Theme Toggle ────────────────────────────────────────────────────────
function toggleTheme() {
  var root = document.documentElement;
  var isLight = root.getAttribute('data-theme') === 'light';
  if (isLight) {
    root.removeAttribute('data-theme');
    localStorage.setItem('dvTheme', 'dark');
  } else {
    root.setAttribute('data-theme', 'light');
    localStorage.setItem('dvTheme', 'light');
  }
  _syncThemeButton();
}

function _syncThemeButton() {
  var label = document.getElementById('themeLabel');
  var btn   = document.getElementById('themeToggle');
  if (!label || !btn) return;
  var isLight = document.documentElement.getAttribute('data-theme') === 'light';
  label.textContent = isLight ? 'DARK' : 'LIGHT';
  btn.title = isLight ? 'Switch to dark theme' : 'Switch to light theme';
}

// Sync label on page load (theme may have been restored before DOMContentLoaded)
document.addEventListener('DOMContentLoaded', _syncThemeButton);
