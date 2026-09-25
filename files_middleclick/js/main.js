/**
 * SPDX-License-Identifier: AGPL-3.0-or-later
 *
 * Chrome on Windows treats a middle-button press on a scrollable area as the
 * start of autoscroll and then never fires `auxclick`, so the Files app's
 * "open in new tab" handler (FileEntryMixin.execDefaultAction) is never
 * called. Cancelling the default action of the `mousedown` for the middle
 * button only, and only on the file name / preview cell, disables autoscroll
 * there so the click reaches Nextcloud. Left and right buttons are untouched.
 */
(function () {
	'use strict'

	document.addEventListener('mousedown', function (event) {
		if (event.button !== 1) {
			return
		}
		var target = event.target
		if (!(target instanceof Element)) {
			return
		}
		if (target.closest('.files-list__row-name')) {
			event.preventDefault()
		}
	}, true)
})()
